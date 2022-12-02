using System.Collections.Concurrent;
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
            _dataHandler.SaveActivityAsync(activity, cancel).ConfigureAwait(false);

        SnTrace.Write(() => $"ActivityQueue: Arrive A{activity.Id}");
        _arrivalQueue.Enqueue(activity);
        _waitToWorkSignal.Set();

        activity.CreateExecutionTask();
        return activity.ExecutionTask;
    }

    private readonly AutoResetEvent _waitToWorkSignal = new AutoResetEvent(false);
    private readonly ConcurrentQueue<Activity> _arrivalQueue = new ConcurrentQueue<Activity>();
    private readonly SortedList<int, Activity> _arrivalSortedList = new SortedList<int, Activity>();
    private long _workCycle = 0;
    private void ControlActivityQueueThread(CancellationToken cancel)
    {
        SnTrace.Write("QueueThread: started");
        while (true)
        {
            if (cancel.IsCancellationRequested)
                break;

            SnTrace.Write($"QueueThread: waiting");

            _waitToWorkSignal.WaitOne();
            if (cancel.IsCancellationRequested)
                break;

            _workCycle++;
            SnTrace.Custom.Write(() => $"QueueThread: works (cycle: {_workCycle}, _arrivalQueue.Count: {_arrivalQueue.Count})");

            while (_arrivalQueue.Count > 0)
            {
                if(_arrivalQueue.TryDequeue(out var arrivedActivity))
                    _arrivalSortedList.Add(arrivedActivity.Id, arrivedActivity);
            }
            SnTrace.Write(() => $"QueueThread: _arrivalSortedList.Count: {_arrivalSortedList.Count}");

            Activity activityToExecute;
            while (null != (activityToExecute = _arrivalSortedList.FirstOrDefault().Value))
            {
                _arrivalSortedList.Remove(activityToExecute.Id);
                var id = activityToExecute.Id;
                SnTrace.Write(() => $"QueueThread: execution start A{id}");
                activityToExecute.ExecutionTask.Start(TaskScheduler.Current);
            }
        }
        SnTrace.Write("QueueThread: finished");
    }
}