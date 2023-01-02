using System.Collections.Concurrent;
using System.Diagnostics;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

/// <summary>
/// Contains information about the executed activities.
/// </summary>
public class CompletionState
{
    /// <summary>
    /// Id of the last executed activity.
    /// </summary>
    public int LastActivityId { get; set; }

    /// <summary>
    /// Contains activity ids that are not executed yet and are lower than the LastActivityId.
    /// </summary>
    public int[] Gaps { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Returns a string that represents the current object.
    /// </summary>
    public override string ToString()
    {
        return $"{LastActivityId}({GapsToString(Gaps, 50, 10)})";
    }

    internal string GapsToString(int[] gaps, int maxCount, int growth)
    {
        if (gaps.Length < maxCount + growth)
            maxCount = gaps.Length;
        return gaps.Length > maxCount
            ? $"{string.Join(",", gaps.Take(maxCount))},... and {gaps.Length - maxCount} additional items"
            : string.Join(",", gaps);
    }
}


public class ActivityQueue : IDisposable
{
    private readonly DataHandler _dataHandler;
    private CancellationTokenSource _activityQueueControllerThreadCancellation;
    private Task _activityQueueThreadController;

    public ActivityQueue(DataHandler dataHandler)
    {
        _dataHandler = dataHandler;
    }

    public void Dispose()
    {
        _activityQueueControllerThreadCancellation.Cancel();
        _waitToWorkSignal.Set();
        Task.Delay(100).GetAwaiter().GetResult();
        _activityQueueThreadController.Dispose();
        _activityQueueControllerThreadCancellation.Dispose();

        //UNDONE: remove comment if the cleaning CompletionState is required when disposing the ActivityQueue
        //_lastExecutedId = 0;
        //_gaps.Clear();
        //_completionState = new CompletionState();

        SnTrace.Write("SAQ: disposed");
    }

    public async Task StartAsync(CancellationToken cancel)
    {
        var dbState = await _dataHandler.LoadCompletionStateAsync(cancel);
        await StartAsync(dbState.CompletionState, dbState.LastDatabaseId, cancel);
    }
    public Task StartAsync(CompletionState uncompleted, int lastDatabaseId, CancellationToken cancel)
    {
        _lastExecutedId = uncompleted.LastActivityId;
        _gaps.Clear();
        if(uncompleted.Gaps != null)
            _gaps.AddRange(uncompleted.Gaps);
        _completionState = new CompletionState { LastActivityId = _lastExecutedId, Gaps = _gaps.ToArray() };

        //UNDONE: Load not executed activities (before gaps after range) and execute them (with partitions).

        // Start worker thread
        _activityQueueControllerThreadCancellation = new CancellationTokenSource();
        _activityQueueThreadController = Task.Factory.StartNew(
            () => ControlActivityQueueThread(_activityQueueControllerThreadCancellation.Token),
            _activityQueueControllerThreadCancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        return Task.CompletedTask;
    }

    // Activity arrival
    public Task ExecuteAsync(Activity activity, CancellationToken cancel)
    {
        if (!activity.FromDatabase && !activity.FromReceiver)
            _dataHandler.SaveActivityAsync(activity, cancel).GetAwaiter().GetResult();

        if(activity.FromReceiver)
            SnTrace.Write(() => $"SAQ: Arrive from receiver #SA{activity.Key}");
        else
            SnTrace.Write(() => $"SAQ: Arrive #SA{activity.Key}");

        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set(); //UNDONE: This instruction should replaced to other places (timer?).

        return activity.CreateTaskForWait();
    }

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<Activity> _arrivalQueue = new();
    private readonly List<Activity> _waitingList = new();
    private readonly List<Activity> _executingList = new();
    private Task? _activityLoaderTask;
    private long _workCycle = 0;

    private int _lastExecutedId;
    private List<int> _gaps = new();
    private CompletionState _completionState;

    public CompletionState GetCompletionState()
    {
        return _completionState;
    }

    private void ControlActivityQueueThread(CancellationToken cancel)
    {
        SnTrace.Write("SAQT: started");
        var finishedList = new List<Activity>(); // temporary
        var lastStartedId = 0;

        while (true)
        {
            try
            {
                if (cancel.IsCancellationRequested)
                    break;

                // Wait if there is nothing to do
                if (_waitingList.Count == 0 && _executingList.Count == 0)
                {
                    SnTrace.Write(() => $"SAQT: waiting for arrival #SA{lastStartedId + 1}");
                    _waitToWorkSignal.WaitOne();
                }

                if (cancel.IsCancellationRequested)
                    break;

                // Continue working
                _workCycle++;
                SnTrace.Custom.Write(() => $"SAQT: works (cycle: {_workCycle}, " +
                                           $"_arrivalQueue.Count: {_arrivalQueue.Count}), " +
                                           $"_executingList.Count: {_executingList.Count}");

                // Move arrived items to the waiting list
                while (_arrivalQueue.Count > 0)
                {
                    if (_arrivalQueue.TryDequeue(out var arrivedActivity))
                        _waitingList.Add(arrivedActivity);
                }

                _waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
                SnTrace.Write(() => $"SAQT: _arrivalSortedList.Count: {_waitingList.Count}");

                // Iterate while the waiting list is not empty or should wait for arrival the next activity.
                // Too early-arrived activities remain in the list (activity.Id > lastStartedId + 1)
                while (_waitingList.Count > 0)
                {
                    var activityToExecute = _waitingList[0];
                    if (activityToExecute.Id <= lastStartedId) // already arrived or executed
                    {
                        _waitingList.RemoveAt(0);
                        var existing = GetAllFromChains(_executingList)
                            .FirstOrDefault(x => x.Id == activityToExecute.Id);
                        if (existing != null)
                        {
                            SnTrace.Write(() => $"SAQT: activity attached to another one: " +
                                                $"#SA{activityToExecute.Key} -> SA{existing.Key}");
                            existing.Attachments.Add(activityToExecute);
                        }
                        else
                        {
                            SnTrace.Write(() =>
                                $"SAQT: execution ignored immediately: #SA{activityToExecute.Key}");
                            activityToExecute.StartFinalizationTask();
                        }
                    }
                    else if (activityToExecute.Id == lastStartedId + 1) // arrived in order
                    {
                        _waitingList.RemoveAt(0);

                        // Discover dependencies
                        foreach (var activityUnderExecution in GetAllFromChains(_executingList))
                            if (activityToExecute.ShouldWaitFor(activityUnderExecution))
                                activityToExecute.WaitFor(activityUnderExecution);

                        // Add to concurrently executable list
                        if (activityToExecute.WaitingFor.Count == 0)
                        {
                            SnTrace.Write(() => $"SAQT: moved to executing list: #SA{activityToExecute.Key}");
                            _executingList.Add(activityToExecute);
                        }

                        // Mark as started even if it is waiting.
                        lastStartedId = activityToExecute.Id;
                    }
                    else
                    {
                        // Load from database async
                        if (_activityLoaderTask == null)
                        {
                            var id = lastStartedId;
                            _activityLoaderTask = Task.Run(() => LoadLastActivities(id + 1, cancel));
                        }

                        break;
                    }
                }

                // Enumerate parallel-executable activities. Dependencies are attached or chained.
                // (activity.WaitingFor.Count == 0)
                foreach (var activity in _executingList)
                {
                    switch (activity.GetExecutionTaskStatus())
                    {
                        // pending
                        case TaskStatus.Created:
                        case TaskStatus.WaitingForActivation:
                        case TaskStatus.WaitingToRun:
                            SnTrace.Write(() => $"SAQT: start execution: #SA{activity.Key}");
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

                foreach (var finishedActivity in finishedList)
                {
                    SnTrace.Write(() => $"SAQT: execution finished: #SA{finishedActivity.Key}");
                    //UNDONE: memorize activity in the ActivityHistory.
                    finishedActivity.StartFinalizationTask();
                    _executingList.Remove(finishedActivity);
                    FinishActivity(finishedActivity);

                    foreach (var attachment in finishedActivity.Attachments)
                    {
                        SnTrace.Write(() => $"SAQT: execution ignored (attachment): #SA{attachment.Key}");
                        attachment.StartFinalizationTask();
                    }

                    finishedActivity.Attachments.Clear();

                    // Handle dependencies: start completely freed dependent activities by adding them to executing-list.
                    foreach (var dependentActivity in finishedActivity.WaitingForMe.ToArray())
                    {
                        SnTrace.Write(() => $"SAQT: activate dependent: #SA{dependentActivity.Key}");
                        dependentActivity.FinishWaiting(finishedActivity);
                        if (dependentActivity.WaitingFor.Count == 0)
                            _executingList.Add(dependentActivity);
                    }

                    _waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
                }

                finishedList.Clear();

                SnTrace.Write(() => $"SAQT: wait a bit.");
                Task.Delay(1).Wait();
            }
            catch (Exception e)
            {
                SnTrace.WriteError(() => e.ToString());

                //UNDONE: Cancel all waiting tasks
                _waitingList.FirstOrDefault()?.StartFinalizationTask();

                break;
            }
        }

        SnTrace.Write("SAQT: finished");
    }
    private void FinishActivity(Activity activity)
    {
        var id = activity.Id;
        if (activity.ExecutionException == null)
        {
            if (id > _lastExecutedId)
            {
                if (id > _lastExecutedId + 1)
                    _gaps.AddRange(Enumerable.Range(_lastExecutedId + 1, id - _lastExecutedId - 1));
                _lastExecutedId = id;
            }
            else
            {
                _gaps.Remove(id);
            }
            _completionState = new CompletionState {LastActivityId = _lastExecutedId, Gaps = _gaps.ToArray()};
            SnTrace.Write(() => $"SAQT: State after finishing SA{id}: {_completionState}");
        }
    }
    private async Task LoadLastActivities(int fromId, CancellationToken cancel)
    {
        var loaded = await _dataHandler.LoadLastActivities(fromId, cancel);
        foreach (var activity in loaded)
        {
            SnTrace.Write(() => $"SAQ: Arrive from database #SA{activity.Key}");
            _arrivalQueue.Enqueue(activity);
        }
        // Unlock loading
        _activityLoaderTask = null;
    }

    private IEnumerable<Activity> GetAllFromChains(List<Activity> roots)
    {
        var flattened = new List<Activity>(roots);
        var index = 0;
        while (index < flattened.Count)
        {
            yield return flattened[index];
            flattened.AddRange(flattened[index].WaitingForMe);
            index++;
        }
    }
}