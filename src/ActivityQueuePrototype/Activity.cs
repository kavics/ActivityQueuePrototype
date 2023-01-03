using System;
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
    [field: NonSerialized, JsonIgnore] private Action<Activity>? _executionCallback; // for prototype only

    [field: NonSerialized, JsonIgnore] private Context Context { get; set; }
    /// <summary>
    /// Gets or sets whether the activity is loaded from the database.
    /// </summary>
    [field: NonSerialized, JsonIgnore] public bool FromDatabase { get; set; }
    /// <summary>
    /// Gets or sets whether the activity comes from the message receiver.
    /// </summary>
    [field: NonSerialized, JsonIgnore] public bool FromReceiver { get; set; }
    /// <summary>
    /// Gets or sets whether the activity is loaded from the database at the system start.
    /// </summary>
    [field: NonSerialized, JsonIgnore] public bool IsUnprocessedActivity { get; set; }

    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingFor { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingForMe { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> Attachments { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal string Key { get; }


    public int Id { get; set; }
    public string TypeName { get; }
    public Exception? ExecutionException { get; private set; }

    public Activity(int id, int delay,
        Func<Activity, Activity, bool>? checkDependencyCallback = null,
        Action<Activity> executionCallback = null)
    {
        Interlocked.Increment(ref _objectId);
        Key = $"{id}-{_objectId}";
        Id = id;
        _delay = delay;
        TypeName = GetType().Name;
        _checkDependencyCallback = checkDependencyCallback;
        _executionCallback = executionCallback;
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
        try
        {
            using var op = SnTrace.StartOperation(() => $"SA: ExecuteInternal #SA{Key} (delay: {_delay})");
            if (_executionCallback != null)
            {
                _executionCallback(this);
                op.Successful = true;
                return;
            }

            Task.Delay(_delay).GetAwaiter().GetResult();
            op.Successful = true;
        }
        catch (Exception e)
        {
            ExecutionException = e;

            // we log this here, because if the activity is not waited for later than the exception would not be logged
            SnTrace.WriteError(() => $"Error during security activity execution. SA{Id} {e}");
        }
    }

    public void WaitFor(Activity olderActivity)
    {
        SnTrace.Write(() => $"SA: Make dependency: #SA{Key} depends from SA{olderActivity.Key}.");
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