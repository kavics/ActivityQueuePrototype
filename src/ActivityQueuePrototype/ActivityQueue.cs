using System.Collections.Concurrent;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

public class ActivityQueue : IDisposable
{
    private readonly DataHandler _dataHandler;
    private readonly CancellationTokenSource _activityQueueThreadControllerCancellation;
    private readonly Task _activityQueueThreadController;

    public ActivityQueue(DataHandler dataHandler)
    {
        _dataHandler = dataHandler;
        _activityQueueThreadControllerCancellation = new CancellationTokenSource();
        _activityQueueThreadController = Task.Factory.StartNew(
            () => ControlActivityQueueThread(_activityQueueThreadControllerCancellation.Token),
            _activityQueueThreadControllerCancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _activityQueueThreadControllerCancellation.Cancel();
        _waitToWorkSignal.Set();
        Task.Delay(100).GetAwaiter().GetResult();
        _activityQueueThreadController.Dispose();
        _activityQueueThreadControllerCancellation.Dispose();
        SnTrace.Write("ActivityQueue: disposed");
    }


    // Activity arrival
    public Task ExecuteAsync(Activity activity, CancellationToken cancel)
    {
        if (!activity.FromDatabase && !activity.FromReceiver)
            _dataHandler.SaveActivityAsync(activity, cancel).GetAwaiter().GetResult();

        SnTrace.Write(() => $"ActivityQueue: Arrive A{activity.Key}");
        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set();

        return activity.CreateExecutionTask();
    }

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<Activity> _arrivalQueue = new();
    private readonly List<Activity> _waitingList = new();
    private readonly List<Activity> _executingList = new();
    private long _workCycle = 0;
    private void ControlActivityQueueThread(CancellationToken cancel)
    {
        SnTrace.Write("QueueThread: started");
        var finishedList = new List<Activity>(); // temporary
        var lastStartedId = 0;
        while (true)
        {
            if (cancel.IsCancellationRequested)
                break;

            SnTrace.Write($"QueueThread: waiting");

            // Wait if there is nothing to do
            if(_waitingList.Count == 0)
                _waitToWorkSignal.WaitOne();
            if (cancel.IsCancellationRequested)
                break;

            // Continue working
            _workCycle++;
            SnTrace.Custom.Write(() => $"QueueThread: works (cycle: {_workCycle}, _arrivalQueue.Count: {_arrivalQueue.Count})");

            // Move arrived items to the waiting list
            while (_arrivalQueue.Count > 0)
            {
                if(_arrivalQueue.TryDequeue(out var arrivedActivity))
                    _waitingList.Add(arrivedActivity);
            }
            _waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
            SnTrace.Write(() => $"QueueThread: _arrivalSortedList.Count: {_waitingList.Count}");

            // Iterate while the waiting list is not empty but exit if there was no operation in the last cycle
            while (_waitingList.Count > 0)
            {
                var activityToExecute = _waitingList[0];
                if (activityToExecute.Id <= lastStartedId) // already arrived or executed
                {
                    _waitingList.RemoveAt(0);
                    //UNDONE: Attach to first instead of start immediately.
                    //UNDONE: Start all attachments without execution after the first is finished.
                    SnTrace.Write(() => $"QueueThread: execution ignored A{activityToExecute.Key}");
                    activityToExecute.StartExecutionTask(false);
                }
                if (activityToExecute.Id == lastStartedId + 1) // arrived in order
                {
                    //UNDONE: Move to an execution list instead of start immediately.
                    //UNDONE: Discover dependencies
                    _waitingList.RemoveAt(0);
                    SnTrace.Write(() => $"QueueThread: moved to executing list: A{activityToExecute.Key}");
                    _executingList.Add(activityToExecute);
                    lastStartedId = activityToExecute.Id;
                    //activityToExecute.StartExecutionTask(true);
                }
                else
                {
                    SnTrace.Write(() => $"QueueThread: waiting for arrival A{lastStartedId + 1}");
                    break;
                }
            }

            //UNDONE: Control execution in execution list. Handle attachments and dependencies.
            foreach (var activity in _executingList)
            {
                switch (activity.GetExecutionTaskStatus())
                {
                    // pending
                    case TaskStatus.Created:
                    case TaskStatus.WaitingForActivation:
                    case TaskStatus.WaitingToRun:
                        SnTrace.Write(() => $"QueueThread: start execution: A{activity.Key}");
                        activity.StartExecutionTask(true);
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
                        //UNDONE: delete from execution list and memorize activity in the execution state.
                        finishedList.Add(activity);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

            }
            foreach (var finishedActivity in finishedList)
            {
                SnTrace.Write(() => $"QueueThread: finished: A{finishedActivity.Key}");
                _executingList.Remove(finishedActivity);
            }

            SnTrace.Write(() => $"QueueThread: wait a bit.");
            Task.Delay(1).Wait();
        }
        SnTrace.Write("QueueThread: finished");
    }
}