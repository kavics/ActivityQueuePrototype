using System.Diagnostics;
using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

[DebuggerDisplay("Activity A{Id}")]
public class Activity
{
    [field: NonSerialized, JsonIgnore] private static int _objectId; // for prototype only

    [field: NonSerialized, JsonIgnore] private readonly int _delay; // for prototype only

    [field: NonSerialized, JsonIgnore] private bool _executionEnabled;
    [field: NonSerialized, JsonIgnore] private Task _executionTask;

    [field: NonSerialized, JsonIgnore] private Context Context { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromDatabase { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromReceiver { get; set; }

    //UNDONE: Missing methods: WaitFor, FinishWaiting, RemoveDependency, Attach, (detach and finish)
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingFor { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingForMe { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> Attached { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal string Key { get; }


    public int Id { get; set; }
    public string TypeName { get; }

    public Activity(int id, int delay)
    {
        Interlocked.Increment(ref _objectId);
        Key = $"{id}-{_objectId}";
        Id = id;
        _delay = delay;
        TypeName = GetType().Name;
    }

    internal Task CreateExecutionTask()
    {
        _executionTask = new Task(ExecuteInternal, TaskCreationOptions.LongRunning);
        return _executionTask;
    }
    internal void StartExecutionTask(bool execute)
    {
        _executionEnabled = execute;
        _executionTask.Start();
    }

    internal TaskStatus GetExecutionTaskStatus() => _executionTask.Status;

    public Task ExecuteAsync(Context context, CancellationToken cancel)
    {
        Context = context;
        return context.ActivityQueue.ExecuteAsync(this, cancel);
    }

    internal void ExecuteInternal()
    {
        if (!_executionEnabled)
        {
            SnTrace.Write(() => $"Activity: execution ignored A{Key}");
            return;
        }

        using var op = SnTrace.StartOperation(() => $"Activity: ExecuteInternal A{Key} (delay: {_delay})");
        Task.Delay(_delay).GetAwaiter().GetResult();
        op.Successful = true;
    }
}