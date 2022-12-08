using System.Diagnostics;
using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

[DebuggerDisplay("Activity A{Id}")]
public class Activity
{
    [field: NonSerialized, JsonIgnore] private static int _objectId; // for prototype only

    [field: NonSerialized, JsonIgnore] private readonly int _delay; // for prototype only

    [field: NonSerialized, JsonIgnore] private Task _executionTask;
    [field: NonSerialized, JsonIgnore] private Task _finalizationTask;

    [field: NonSerialized, JsonIgnore] private Func<Activity, Activity, bool>? _checkDependencyCallback; // for prototype only

    [field: NonSerialized, JsonIgnore] private Context Context { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromDatabase { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromReceiver { get; set; }

    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingFor { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingForMe { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> Attachments { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal string Key { get; }


    public int Id { get; set; }
    public string TypeName { get; }

    public Activity(int id, int delay, Func<Activity, Activity, bool>? checkDependencyCallback = null)
    {
        Interlocked.Increment(ref _objectId);
        Key = $"{id}-{_objectId}";
        Id = id;
        _delay = delay;
        TypeName = GetType().Name;
        _checkDependencyCallback = checkDependencyCallback;
    }

    internal Task CreateTaskForWait()
    {
        _finalizationTask = new Task(() => { /* do nothing */ }, TaskCreationOptions.LongRunning);
        return _finalizationTask;
    }
    internal void StartExecutionTask()
    {
        _executionTask = new Task(ExecuteInternal, TaskCreationOptions.LongRunning);
        _executionTask.Start();
    }
    internal void StartFinalizationTask()
    {
        _finalizationTask?.Start();
    }

    internal TaskStatus GetExecutionTaskStatus() => _executionTask?.Status ?? TaskStatus.Created;

    internal bool ShouldWaitFor(Activity olderActivity) => _checkDependencyCallback?.Invoke(this, olderActivity) ?? false;

    public Task ExecuteAsync(Context context, CancellationToken cancel)
    {
        Context = context;
        return context.ActivityQueue.ExecuteAsync(this, cancel);
    }

    internal void ExecuteInternal()
    {
        using var op = SnTrace.StartOperation(() => $"Activity: ExecuteInternal A{Key} (delay: {_delay})");
        Task.Delay(_delay).GetAwaiter().GetResult();
        op.Successful = true;
    }

    public void WaitFor(Activity olderActivity)
    {
        SnTrace.Write(() => $"Activity: Make dependency: A{Key} depends from {olderActivity.Key}.");
        // this method should called from thread safe block.
        if (WaitingFor.All(x => x.Id != olderActivity.Id))
            WaitingFor.Add(olderActivity);
        if (olderActivity.WaitingForMe.All(x => x.Id != Id))
            olderActivity.WaitingForMe.Add(this);
    }
    public void FinishWaiting(Activity finishedActivity)
    {
        // this method must called from thread safe block.
        RemoveDependency(WaitingFor, finishedActivity);
        RemoveDependency(finishedActivity.WaitingForMe, this);
    }
    private void RemoveDependency(List<Activity> dependencyList, Activity activity)
    {
        // this method must called from thread safe block.
        dependencyList.RemoveAll(x => x.Id == activity.Id);
    }

    public Activity Clone()
    {
        return new Activity(Id, _delay, _checkDependencyCallback);
    }
}