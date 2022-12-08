using System.Collections.Concurrent;
using System.Diagnostics;
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

        return activity.CreateTaskForWait();
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
            try
            {
                if (cancel.IsCancellationRequested)
                    break;

                // Wait if there is nothing to do
                if (_waitingList.Count == 0 && _executingList.Count == 0)
                {
                    SnTrace.Write(() => $"QueueThread: waiting for arrival A{lastStartedId + 1}");
                    _waitToWorkSignal.WaitOne();
                }

                if (cancel.IsCancellationRequested)
                    break;

                // Continue working
                _workCycle++;
                SnTrace.Custom.Write(() => $"QueueThread: works (cycle: {_workCycle}, " +
                                           $"_arrivalQueue.Count: {_arrivalQueue.Count}), " +
                                           $"_executingList.Count: {_executingList.Count}");

                // Move arrived items to the waiting list
                while (_arrivalQueue.Count > 0)
                {
                    if (_arrivalQueue.TryDequeue(out var arrivedActivity))
                        _waitingList.Add(arrivedActivity);
                }

                _waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));
                SnTrace.Write(() => $"QueueThread: _arrivalSortedList.Count: {_waitingList.Count}");

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
                            SnTrace.Write(() => $"QueueThread: activity attached to another one: " +
                                                $"A{activityToExecute.Key} -> A{existing.Key}");
                            existing.Attachments.Add(activityToExecute);
                        }
                        else
                        {
                            SnTrace.Write(() =>
                                $"QueueThread: execution ignored immediately: A{activityToExecute.Key}");
                            activityToExecute.StartFinalizationTask();
                        }
                    }
                    else if (activityToExecute.Id == lastStartedId + 1) // arrived in order
                    {
                        _waitingList.RemoveAt(0);

                        // Discover dependencies
                        //UNDONE: discover dependencies in deep (see WaitForMe list too)
                        foreach (var activityUnderExecution in GetAllFromChains(_executingList))
                            if (activityToExecute.ShouldWaitFor(activityUnderExecution))
                                activityToExecute.WaitFor(activityUnderExecution);

                        // Add to concurrently executable list
                        if (activityToExecute.WaitingFor.Count == 0)
                        {
                            SnTrace.Write(() => $"QueueThread: moved to executing list: A{activityToExecute.Key}");
                            _executingList.Add(activityToExecute);
                        }

                        // Mark as started even if it is waiting.
                        lastStartedId = activityToExecute.Id;
                    }
                    else
                    {
                        //UNDONE: load from db
                        break;
                    }
                }

                // Enumerate parallel-executable activities. Dependencies are attached or chained.
                foreach (var activity in _executingList)
                {
                    if (activity.WaitingFor.Count > 0)
                        continue;

                    switch (activity.GetExecutionTaskStatus())
                    {
                        // pending
                        case TaskStatus.Created:
                        case TaskStatus.WaitingForActivation:
                        case TaskStatus.WaitingToRun:
                            SnTrace.Write(() => $"QueueThread: start execution: A{activity.Key}");
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
                    SnTrace.Write(() => $"QueueThread: execution finished: A{finishedActivity.Key}");
                    //UNDONE: memorize activity in the ActivityHistory.
                    finishedActivity.StartFinalizationTask();
                    _executingList.Remove(finishedActivity);

                    foreach (var attachment in finishedActivity.Attachments)
                    {
                        SnTrace.Write(() => $"QueueThread: execution ignored (attachment): A{attachment.Key}");
                        attachment.StartFinalizationTask();
                    }

                    finishedActivity.Attachments.Clear();

                    // Handle dependencies: start completely freed dependent activities by adding them to executing-list.
                    foreach (var dependentActivity in finishedActivity.WaitingForMe.ToArray())
                    {
                        SnTrace.Write(() => $"QueueThread: activate dependent: A{dependentActivity.Key}");
                        dependentActivity.FinishWaiting(finishedActivity);
                        if (dependentActivity.WaitingFor.Count == 0)
                            _executingList.Add(dependentActivity);
                    }

                    _waitingList.Sort((x, y) => x.Id.CompareTo(y.Id));

                }

                finishedList.Clear();

                SnTrace.Write(() => $"QueueThread: wait a bit.");
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

        SnTrace.Write("QueueThread: finished");
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