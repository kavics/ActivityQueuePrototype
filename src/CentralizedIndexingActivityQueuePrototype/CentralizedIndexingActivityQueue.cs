using System.Collections.Concurrent;
using System.Configuration;
using System.Diagnostics;
using SenseNet.Diagnostics;

namespace CentralizedIndexingActivityQueuePrototype;

public class CentralizedIndexingActivityQueue : IDisposable
{
    private readonly DataStore _dataStore;
    private readonly IIndexingActivityFactory _indexingActivityFactory;
    private CancellationTokenSource _activityQueueControllerThreadCancellation;
    private Task _activityQueueThreadController;

    public CentralizedIndexingActivityQueue(DataStore dataHandler, IIndexingActivityFactory indexingActivityFactory)
    {
        _dataStore = dataHandler;
        _indexingActivityFactory = indexingActivityFactory;
    }

    public void Dispose()
    {
        if (_timer != null)
        {
            _timer.Enabled = false;
            _timer.Dispose();
            _timer = null;
        }

        _activityQueueControllerThreadCancellation.Cancel();
        _waitToWorkSignal.Set();
        Task.Delay(100).GetAwaiter().GetResult();
        _activityQueueThreadController.Dispose();
        _activityQueueControllerThreadCancellation.Dispose();

        SnTrace.Write("CIAQ: disposed");
    }
    private void Polling()
    {
        //UNDONE: CIAQ: RefreshLocks
        //RefreshLocks();

        //UNDONE: CIAQ: DeleteFinishedActivitiesOccasionally
        //DeleteFinishedActivitiesOccasionally();

        //UNDONE: CIAQ: IsPollingEnabled
        //if (!IsPollingEnabled())
        //    return;

        if ((DateTime.UtcNow - _lastExecutionTime) > _healthCheckPeriod)
        {
            try
            {
                //UNDONE: CIAQ: DisablePolling
                //DisablePolling();
                SnTrace.Write("CIAQ: polling timer beats.");
                _waitToWorkSignal.Set();
                // original: ExecuteActivities(null, false);
            }
            finally
            {
                //UNDONE: CIAQ: EnablePolling
                //EnablePolling();
            }
        }
    }

    public async Task StartAsync(TextWriter? consoleOut, CancellationToken cancel)
    {
        using var op = SnTrace.StartOperation("CIAQ: STARTUP.");

        // Start worker thread
        _activityQueueControllerThreadCancellation = new CancellationTokenSource();
        _activityQueueThreadController = Task.Factory.StartNew(
            () => ControlActivityQueueThread(_activityQueueControllerThreadCancellation.Token),
            _activityQueueControllerThreadCancellation.Token, TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        //UNDONE: CIAQ: executing unprocessed activities in system start sequence.
        // ...

        // every period starts now
        _lastLockRefreshTime = DateTime.UtcNow;
        _lastExecutionTime = DateTime.UtcNow;
        _lastDeleteFinishedTime = DateTime.UtcNow;

        _timer = new System.Timers.Timer(_hearthBeatMilliseconds);
        _timer.Elapsed += Timer_Elapsed;
        _timer.Disposed += Timer_Disposed;
        _timer.Enabled = true;

        var msg = $"CIAQ: polling timer started. Heartbeat: {_hearthBeatMilliseconds} milliseconds";
        SnTrace.IndexQueue.Write(msg);
        consoleOut?.WriteLine(msg);

        op.Successful = true;
    }
    private void Timer_Disposed(object? sender, EventArgs e)
    {
        _timer.Elapsed -= Timer_Elapsed;
        _timer.Disposed -= Timer_Disposed;
    }
    private void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Polling();
    }

    // Activity arrival
    public Task ExecuteActivity(IIndexingActivity activity)
    {
        SnTrace.Write(() => $"CIAQ: Arrive #IA{activity.Key}");

        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set(); //UNDONE: CIAQ: This instruction should replaced to other places (timer?).

        return activity.CreateTaskForWait();
    }


    private readonly int _maxCount = 10;

    private readonly int _runningTimeoutInSeconds = 120; //Configuration.Indexing.IndexingActivityTimeoutInSeconds;
    //private readonly int _lockRefreshPeriodInMilliseconds;
    private readonly int _hearthBeatMilliseconds = 1000;

    //private readonly TimeSpan _waitingPollingPeriod = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _healthCheckPeriod = TimeSpan.FromMinutes(2);
    //private readonly TimeSpan _deleteFinishedPeriod = TimeSpan.FromMinutes(23);
    //UNDONE: CIAQ: ActiveTaskLimit (used in conditional expressions: ... && _activeTasks < ActiveTaskLimit)
    //private const int ActiveTaskLimit = 43;

    private System.Timers.Timer _timer;
    private DateTime _lastExecutionTime;
    private DateTime _lastLockRefreshTime;
    private DateTime _lastDeleteFinishedTime;
    //UNDONE: CIAQ: _activeTasks (used in conditional expressions: ... && _activeTasks < ActiveTaskLimit)
    //private volatile int _activeTasks;
    //private int _pollingBlockerCounter;



    /* =================================================================================================== */

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<IIndexingActivity> _arrivalQueue = new();
    private readonly List<IIndexingActivity> _waitingList = new(); // orig: _waitingActivities
    private readonly List<IIndexingActivity> _executingList = new();
    //private Task? _activityLoaderTask;
    private int _lastExecutedId; //UNDONE: CIAQ: Delete _lastExecutedId if not needed.
    private long _workCycle = 0;

    private void ControlActivityQueueThread(CancellationToken cancel)
    // original: private int ExecuteActivities(IndexingActivityBase waitingActivity, bool systemStart)
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

                if (true /* !limited */)
                {
                    // load
                    var waitingActivityIds = _waitingList.Select(x => x.Id).ToArray();
                    var result = _dataStore.LoadExecutableIndexingActivitiesAsync(
                        _indexingActivityFactory,
                        _maxCount,
                        _runningTimeoutInSeconds,
                        waitingActivityIds, CancellationToken.None).GetAwaiter().GetResult();
                    var loadedActivities = result.Activities;
                    var finishedActivityIds = result.FinishedActivitiyIds;
                    SnTrace.Write(() => $"CIAQ: loaded: {0} ({loadedActivities.Length}), " +
                                        $"waiting: {string.Join(", ", loadedActivities.Select(la => la.Id))}, " +
                                        $"finished: {finishedActivityIds.Length}, tasks: {"???"/*_activeTasks*/}");

                    // release finished activities
                    if (finishedActivityIds.Length > 0)
                    {
                        foreach (var finishedActivityId in finishedActivityIds)
                        {
                            var finishedActivity = _waitingList.FirstOrDefault(x => x.Id == finishedActivityId);
                            if (finishedActivity != null)
                            {
                                _waitingList.Remove(finishedActivity);
                                finishedActivity.Finish2();
                            }
                        }
                    }

                    // execute loaded
                    if (loadedActivities.Any())
                    {
                            // execute loaded activities
                            foreach (var loadedActivity in loadedActivities)
                            {
//UNDONE: CIAQ: Setting IsUnprocessedActivity is not implemented
//if (systemStart)
//    loadedActivity.IsUnprocessedActivity = true;

                                // If a loaded activity is the same as any of the already waiting activities, we have to
                                // drop the loaded instance and execute the waiting instance, otherwise the algorithm
                                // would not notice when the activity is finished and the finish signal is released.
                                IIndexingActivity executableActivity;

                                var otherWaitingActivity = _waitingList.FirstOrDefault(x => x.Id == loadedActivity.Id);
                                if (otherWaitingActivity != null)
                                {
                                    // Found in the waiting list: drop the loaded one and execute the waiting.
                                    executableActivity = otherWaitingActivity;
                                    SnTrace.Write($"CIAQ: Loaded A{loadedActivity.Id} found in the waiting list.");
                                }
                                else
                                {
                                    // If a loaded activity is not in the waiting list, we have to add it here
                                    // so that other threads may find it and be able to attach to it.
                                    _waitingList.Add(loadedActivity);
                                    executableActivity = loadedActivity;
                                    SnTrace.Write($"CIAQ: Adding loaded A{loadedActivity.Id} to waiting list.");
                                }
throw new NotImplementedException();
//System.Threading.Tasks.Task.Run(() => Execute(executableActivity));
                            }
                    }

                }

                // wait a bit
                SnTrace.Write(() => $"CIAQT: wait a bit.");
                Task.Delay(100).Wait();



//// very simplified
//while (_waitingList.Count > 0)
//{
//    var activityToExecute = _waitingList[0];
//    _waitingList.RemoveAt(0);
//    _executingList.Add(activityToExecute);
//}
//SuperviseExecutions(_executingList, finishedList);
//ManageFinishedActivities(finishedList, _executingList);
//_lastExecutionTime = DateTime.UtcNow;
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
                        break; //UNDONE: CIAQ: are you sure to exit here?
                    }
                }

                // Enumerate parallel-executable activities. Dependencies are attached or chained.
                // (activity.WaitingFor.Count == 0)
                SuperviseExecutions(_executingList, finishedList); // manage pending, execution and finished states

                // Releases starter threads with attachments and activates dependent items
                ManageFinishedActivities(finishedList, _executingList);

                // End of cycle
                SnTrace.Write(() => $"SAQT: wait a bit.");
                Task.Delay(1).Wait();
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
        // Move arrived items to the waiting list or attach the existing one.
        while (arrivalQueue.Count > 0)
        {
            if (arrivalQueue.TryDequeue(out var arrivedActivity))
            {
                var existing = waitingList.FirstOrDefault(x => x.Id == arrivedActivity.Id);
                if (existing != null)
                    existing.Attach(arrivedActivity);
                else
                    waitingList.Add(arrivedActivity);
            }
        }

        //waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
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