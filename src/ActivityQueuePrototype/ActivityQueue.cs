using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

internal class ActivityQueue : IDisposable
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

        SnTrace.Write(() => $"ActivityQueue: Arrive A{activity.Id}");
        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set();

        activity.CreateExecutionTask();
        return activity.ExecutionTask;
    }

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<Activity> _arrivalQueue = new();
    private readonly List<Activity> _arrivalSortedList = new();
    private long _workCycle = 0;
    private void ControlActivityQueueThread(CancellationToken cancel)
    {
        SnTrace.Write("QueueThread: started");
        var lastStartedId = 0;
        while (true)
        {
            if (cancel.IsCancellationRequested)
                break;

            SnTrace.Write($"QueueThread: waiting");

            // Wait if there is nothing to do
            if(_arrivalSortedList.Count == 0)
                _waitToWorkSignal.WaitOne();
            if (cancel.IsCancellationRequested)
                break;

            // Continue working
            _workCycle++;
            SnTrace.Custom.Write(() => $"QueueThread: works (cycle: {_workCycle}, _arrivalQueue.Count: {_arrivalQueue.Count})");

            // Move arrived items to the sorted list
            while (_arrivalQueue.Count > 0)
            {
                if(_arrivalQueue.TryDequeue(out var arrivedActivity))
                    _arrivalSortedList.Add(arrivedActivity);
            }
            _arrivalSortedList.Sort((x, y) => x.Id.CompareTo(y.Id));
            SnTrace.Write(() => $"QueueThread: _arrivalSortedList.Count: {_arrivalSortedList.Count}");

            // Iterate while the sorted list is not empty
            while (_arrivalSortedList.Count > 0)
            {
                var activityToExecute = _arrivalSortedList[0];
                if (activityToExecute.Id <= lastStartedId)
                {
                    _arrivalSortedList.RemoveAt(0);
                    SnTrace.Write(() => $"QueueThread: execution ignored A{activityToExecute.Id}");
                    activityToExecute.Ignore();
                    activityToExecute.ExecutionTask.Start(TaskScheduler.Current);
                }
                if (activityToExecute.Id == lastStartedId + 1)
                {
                    _arrivalSortedList.RemoveAt(0);
                    SnTrace.Write(() => $"QueueThread: execution start A{activityToExecute.Id}");
                    activityToExecute.ExecutionTask.Start(TaskScheduler.Current);
                    lastStartedId = activityToExecute.Id;
                }
                else
                {
                    SnTrace.Write(() => $"QueueThread: waiting for arrival A{lastStartedId + 1}");
                    break;
                }
            }
            SnTrace.Write(() => $"QueueThread: wait a bit.");
            Task.Delay(1).Wait();
        }
        SnTrace.Write("QueueThread: finished");
    }
}