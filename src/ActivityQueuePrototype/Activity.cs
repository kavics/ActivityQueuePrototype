using System.Diagnostics;
using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

[DebuggerDisplay("Activity A{Id}")]
internal class Activity
{
    [field: NonSerialized, JsonIgnore] private readonly int _delay;

    [field: NonSerialized, JsonIgnore] private Context Context { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromDatabase { get; set; }
    [field: NonSerialized, JsonIgnore] public bool FromReceiver { get; set; }
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingFor { get; private set; } = new List<Activity>();
    [field: NonSerialized, JsonIgnore] internal List<Activity> WaitingForMe { get; private set; } = new List<Activity>();

    [field: NonSerialized, JsonIgnore] public Task ExecutionTask { get; private set; } //UNDONE: Delete/hide ExecutionTask property if tested

    public int Id { get; set; }
    public string TypeName { get; }

    public Activity(int id, int delay)
    {
        Id = id;
        _delay = delay;
        TypeName = GetType().Name;
    }

    internal Task CreateExecutionTask()
    {
        ExecutionTask = new Task(ExecuteInternal, TaskCreationOptions.LongRunning);
        return ExecutionTask;
    }

    private bool _ignored;
    internal void Ignore()
    {
        _ignored = true;
    }

    public Task ExecuteAsync(Context context, CancellationToken cancel)
    {
        Context = context;
        return context.ActivityQueue.ExecuteAsync(this, cancel);
    }

    internal void ExecuteInternal()
    {
        if (_ignored)
        {
            SnTrace.Write(() => $"Activity: execution ignored A{Id}");
            return;
        }

        using var op = SnTrace.StartOperation(() => $"Activity: ExecuteInternal A{Id} (delay: {_delay})");
        Task.Delay(_delay).GetAwaiter().GetResult();
        op.Successful = true;
    }
}