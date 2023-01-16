using System.Diagnostics;
using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace SecurityActivityQueuePrototype;

[DebuggerDisplay("Activity A{Id}")]
public class SecurityActivity
{
    [field: NonSerialized, JsonIgnore] private static int _objectId; // only for the prototype

    [field: NonSerialized, JsonIgnore] private readonly int _delay; // only for the prototype

    [field: NonSerialized, JsonIgnore] private Task? _executionTask;
    [field: NonSerialized, JsonIgnore] private Task? _finalizationTask;

    [field: NonSerialized, JsonIgnore] private Func<SecurityActivity, SecurityActivity, bool>? _checkDependencyCallback; // only for the prototype
    [field: NonSerialized, JsonIgnore] private Action<SecurityActivity>? _executionCallback; // only for the prototype

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

    [field: NonSerialized, JsonIgnore] internal List<SecurityActivity> WaitingFor { get; private set; } = new List<SecurityActivity>();
    [field: NonSerialized, JsonIgnore] internal List<SecurityActivity> WaitingForMe { get; private set; } = new List<SecurityActivity>();
    [field: NonSerialized, JsonIgnore] internal List<SecurityActivity> Attachments { get; private set; } = new List<SecurityActivity>();
    [field: NonSerialized, JsonIgnore] internal string Key { get; }
    [field: NonSerialized, JsonIgnore] internal CancellationToken CancellationToken { get; set; }


    public int Id { get; set; }
    public string TypeName { get; }
    public Exception? ExecutionException { get; private set; }

    public SecurityActivity(int id, int delay,
        Func<SecurityActivity, SecurityActivity, bool>? checkDependencyCallback = null,
        Action<SecurityActivity>? executionCallback = null)
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
        _finalizationTask = new Task(() => { /* do nothing */ }, CancellationToken, TaskCreationOptions.LongRunning); //UNDONE: ?? avoid a lot of LongRunning
        return _finalizationTask;
    }
    internal void StartExecutionTask()
    {
        _executionTask = ExecuteInternalAsync(CancellationToken);
    }
    internal void StartFinalizationTask()
    {
        _finalizationTask?.Start();
    }

    internal TaskStatus? GetExecutionTaskStatus() => _executionTask?.Status;

    internal bool ShouldWaitFor(SecurityActivity olderActivity) => _checkDependencyCallback?.Invoke(this, olderActivity) ?? false;

    public Task ExecuteAsync(Context context, CancellationToken cancel) => context.ActivityQueue.ExecuteAsync(this, cancel);

    internal async Task ExecuteInternalAsync(CancellationToken cancel)
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

            await Task.Delay(_delay, cancel).ConfigureAwait(false);
            op.Successful = true;
        }
        catch (TaskCanceledException)
        {
            SnTrace.Write(() => $"A security activity execution CANCELED. #SA{Id}");
        }
        catch (Exception e)
        {
            ExecutionException = e;

            // we log this here, because if the activity is not waited for later than the exception would not be logged
            SnTrace.WriteError(() => $"Error during security activity execution. #SA{Id} {e}");
        }
    }

    public void WaitFor(SecurityActivity olderActivity)
    {
        SnTrace.Write(() => $"SA: Make dependency: #SA{Key} depends from SA{olderActivity.Key}.");
        // this method should called from thread safe block.
        if (WaitingFor.All(x => x.Id != olderActivity.Id))
            WaitingFor.Add(olderActivity);
        if (olderActivity.WaitingForMe.All(x => x.Id != Id))
            olderActivity.WaitingForMe.Add(this);
    }
    public void FinishWaiting(SecurityActivity finishedActivity)
    {
        // this method must called from thread safe block.
        RemoveDependency(WaitingFor, finishedActivity);
        RemoveDependency(finishedActivity.WaitingForMe, this);
    }
    private void RemoveDependency(List<SecurityActivity> dependencyList, SecurityActivity activity)
    {
        // this method must called from thread safe block.
        dependencyList.RemoveAll(x => x.Id == activity.Id);
    }

    public SecurityActivity Clone()
    {
        return new SecurityActivity(Id, _delay, _checkDependencyCallback);
    }
}