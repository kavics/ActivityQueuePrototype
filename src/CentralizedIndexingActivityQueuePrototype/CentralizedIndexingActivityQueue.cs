using System.Collections.Concurrent;
using SenseNet.Diagnostics;

namespace CentralizedIndexingActivityQueuePrototype;

public class CentralizedIndexingActivityQueue : IDisposable
{
    private readonly DataStore _dataHandler;
    private readonly IIndexingActivityFactory _indexingActivityFactory;
    private CancellationTokenSource _activityQueueControllerThreadCancellation;
    private Task _activityQueueThreadController;

    public CentralizedIndexingActivityQueue(DataStore dataHandler, IIndexingActivityFactory indexingActivityFactory)
    {
        _dataHandler = dataHandler;
        _indexingActivityFactory = indexingActivityFactory;
    }

    //UNDONE: CIAQ: Dispose
    public void Dispose()
    {
        _activityQueueControllerThreadCancellation.Cancel();
        _waitToWorkSignal.Set();
        Task.Delay(100).GetAwaiter().GetResult();
        _activityQueueThreadController.Dispose();
        _activityQueueControllerThreadCancellation.Dispose();

        SnTrace.Write("CIAQ: disposed");
    }

    //UNDONE: CIAQ: StartAsync
    public async Task StartAsync(CancellationToken cancel)
    {
        // Start worker thread
        _activityQueueControllerThreadCancellation = new CancellationTokenSource();
        _activityQueueThreadController = Task.Factory.StartNew(
            () => ControlActivityQueueThread(_activityQueueControllerThreadCancellation.Token),
            _activityQueueControllerThreadCancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    // Activity arrival
    public Task ExecuteActivity(IIndexingActivity activity)
    {
        SnTrace.Write(() => $"CIAQ: Arrive #IA{activity.Key}");

        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set(); //UNDONE: CIAQ: This instruction should replaced to other places (timer?).

        return activity.CreateTaskForWait();
    }

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<IIndexingActivity> _arrivalQueue = new();
    private readonly List<IIndexingActivity> _waitingList = new(); // orig: _waitingActivities
    private readonly List<IIndexingActivity> _executingList = new();
    //private Task? _activityLoaderTask;
    private long _workCycle = 0;

    private int _lastExecutedId;
    //private List<int> _gaps = new();
    //private CompletionState _completionState;

    /* =================================================================================================== */

    private void ControlActivityQueueThread(CancellationToken cancel)
    {
        SnTrace.Write("CIAQT: started");
        var finishedList = new List<IIndexingActivity>(); // temporary
        var lastStartedId = _lastExecutedId;

        while (true)
        {
            try
            {
                if (cancel.IsCancellationRequested)
                    break;

                // Wait if there is nothing to do
                if (_waitingList.Count == 0 && _executingList.Count == 0)
                {
                    SnTrace.Write(() => $"CIAQT: waiting for arrival");
                    _waitToWorkSignal.WaitOne();
                }

                if (cancel.IsCancellationRequested)
                    break;
                _workCycle++;
                SnTrace.Write(() => $"CIAQT: works (cycle: {_workCycle}, " +
                                           $"_arrivalQueue.Count: {_arrivalQueue.Count}), " +
                                           $"_executingList.Count: {_executingList.Count}");

                LineUpArrivedActivities(_arrivalQueue, _waitingList);

// very simplified
while (_waitingList.Count > 0)
{
    var activityToExecute = _waitingList[0];
    _waitingList.RemoveAt(0);
    _executingList.Add(activityToExecute);
}
SuperviseExecutions(_executingList, finishedList);
ManageFinishedActivities(finishedList, _executingList);


                /* from SecurityActivityQueue
                // Iterate while the waiting list is not empty or should wait for arrival the next activity.
                // Too early-arrived activities remain in the list (activity.Id > lastStartedId + 1)
                // If the current activity is "unprocessed" (startup mode), it needs to process instantly and
                //   skip not-loaded activities because the activity may process a gap.
                while (_waitingList.Count > 0)
                {
                    var activityToExecute = _waitingList[0];
                    if (!activityToExecute.IsUnprocessedActivity && activityToExecute.Id <= lastStartedId) // already arrived or executed
                    {
                        AttachOrIgnore(activityToExecute, _waitingList, _executingList);
                    }
                    else if (activityToExecute.IsUnprocessedActivity || activityToExecute.Id == lastStartedId + 1) // arrived in order
                    {
                        lastStartedId = ExecuteOrChain(activityToExecute, _waitingList, _executingList);
                    }
                    else
                    {
                        // Load the missed out activities from database or skip this if it is happening right now.
                        _activityLoaderTask ??= Task.Run(() => LoadLastActivities(lastStartedId + 1, cancel));
                        break; //UNDONE: SAQ: are you sure to exit here?
                    }
                }

                // Enumerate parallel-executable activities. Dependencies are attached or chained.
                // (activity.WaitingFor.Count == 0)
                SuperviseExecutions(_executingList, finishedList); // manage pending, execution and finished states

                // Releases starter threads with attachments and activates dependent items
                ManageFinishedActivities(finishedList, _executingList);

                // End of cycle
                SnTrace.Write(
                */
            }
            catch (Exception e)
            {
                SnTrace.WriteError(() => e.ToString());

////UNDONE: CIAQ: Cancel all waiting tasks
//_waitingList.FirstOrDefault()?.StartFinalizationTask();

                break;
            }

        }
    }
    private void LineUpArrivedActivities(ConcurrentQueue<IIndexingActivity> arrivalQueue, List<IIndexingActivity> waitingList)
    {
        // Move arrived items to the waiting list
        while (arrivalQueue.Count > 0)
        {
            if (arrivalQueue.TryDequeue(out var arrivedActivity))
                waitingList.Add(arrivedActivity);
        }

        waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
        SnTrace.Write(() => $"CIAQT: arrivalSortedList.Count: {waitingList.Count}");
    }
    private void SuperviseExecutions(List<IIndexingActivity> executingList, List<IIndexingActivity> finishedList)
    {
        foreach (var activity in executingList)
        {
            switch (activity.GetExecutionTaskStatus())
            {
                // pending
                case TaskStatus.Created:
                case TaskStatus.WaitingForActivation:
                case TaskStatus.WaitingToRun:
                    SnTrace.Write(() => $"CIAQT: start execution: #IA{activity.Key}");
                    activity.StartExecutionTask();
                    break;

                // executing
                case TaskStatus.Running:
                case TaskStatus.WaitingForChildrenToComplete:
                    // do nothing?
                    break;

                // finished
                case TaskStatus.RanToCompletion:
                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    finishedList.Add(activity);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    // simplified
    private void ManageFinishedActivities(List<IIndexingActivity> finishedList, List<IIndexingActivity> executingList)
    {
        foreach (var finishedActivity in finishedList)
        {
            SnTrace.Write(() => $"CIAQT: execution finished: #IA{finishedActivity.Key}");
            finishedActivity.StartFinalizationTask();
            executingList.Remove(finishedActivity);
        }

        finishedList.Clear();
    }

}