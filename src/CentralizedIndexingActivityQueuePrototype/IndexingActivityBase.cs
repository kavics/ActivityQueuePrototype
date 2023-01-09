using Newtonsoft.Json;
using SenseNet.Diagnostics;

namespace CentralizedIndexingActivityQueuePrototype;

public enum IndexingActivityType
{
    /// <summary>
    /// Add brand new document. Value = 1.
    /// </summary>
    AddDocument = 1,
    /// <summary>
    /// Add brand new document set. Value = 2.
    /// </summary>
    AddTree = 2,
    /// <summary>
    /// Update the document. Value = 3.
    /// </summary>
    UpdateDocument = 3,
    /// <summary>
    /// Remove the tree. Value = 4.
    /// </summary>
    RemoveTree = 4,
    /// <summary>
    /// Rebuild the index. Value = 5.
    /// </summary>
    Rebuild,
    /// <summary>
    /// Indicates that the index was restored. Value = 6.
    /// </summary>
    Restore
}

public enum IndexingActivityRunningState
{
    /// <summary>
    /// Wait for execution.
    /// </summary>
    Waiting,
    /// <summary>
    /// It is being executed right now.
    /// </summary>
    Running,
    /// <summary>
    /// Executed.
    /// </summary>
    Done
}

public interface IQueuedActivity
{
    Task CreateTaskForWait();
    void StartExecutionTask();
    void StartFinalizationTask();
    TaskStatus GetExecutionTaskStatus();

    void Attach(IIndexingActivity activity);
    void Finish2(); // Different from original Finish() that uses an AsyncAutoResetEvent
}
public interface IIndexingActivity : IQueuedActivity
{
    int Id { get; set; }
    string Key { get; }
    IndexingActivityType ActivityType { get; set; }
    IndexingActivityRunningState RunningState { get; set; }
    DateTime? LockTime { get; set; }
    bool FromDatabase { get; set; }
    bool IsUnprocessedActivity { get; set; }
}

public abstract class IndexingActivityBase : IIndexingActivity
{
    private static int _objectId; // only for the prototype
    private readonly int _delay = 0; // only for the prototype
    private readonly int _thisObjectId; // only for the prototype

    public int Id { get; set; }
    public string Key => $"{Id}-{_thisObjectId}"; // only for the prototype
    public IndexingActivityType ActivityType { get; set; }
    public IndexingActivityRunningState RunningState { get; set; }
    public DateTime? LockTime { get; set; }
    public bool FromDatabase { get; set; }
    public bool IsUnprocessedActivity { get; set; }

    internal IIndexingActivity? AttachedActivity { get; private set; }



    protected IndexingActivityBase(IndexingActivityType activityType)
    {
        ActivityType = activityType;
        _thisObjectId = Interlocked.Increment(ref _objectId);
    }



    private Task _executionTask;
    private Task _finalizationTask;
    public TaskStatus GetExecutionTaskStatus() => _executionTask?.Status ?? TaskStatus.Created;
    public void Attach(IIndexingActivity activity)
    {
        if (AttachedActivity != null)
            AttachedActivity.Attach(activity);
        else
            AttachedActivity = activity;
    }

    public void Finish2()
    {
        // finalize attached activities first
        if (AttachedActivity != null)
        {
            SnTrace.Write("Attached IndexingActivity finished: A{0}.", AttachedActivity.Id);
            AttachedActivity.Finish2();
        }

        StartFinalizationTask();
        SnTrace.Write("IndexingActivity finished: A{0}.", Id);
    }

    public Task CreateTaskForWait()
    {
        _finalizationTask = new Task(() => { /* do nothing */ }, TaskCreationOptions.LongRunning);
        return _finalizationTask;
    }
    public void StartExecutionTask()
    {
        _executionTask = new Task(ExecuteInternal, TaskCreationOptions.LongRunning);
        _executionTask.Start();
    }
    public void StartFinalizationTask()
    {
        _finalizationTask?.Start();
    }






    internal void ExecuteInternal()
    {
        //try
        //{
        //    using var op = SnTrace.StartOperation(() => $"SA: ExecuteInternal #SA{Key} (delay: {_delay})");
        //    if (_executionCallback != null)
        //    {
        //        _executionCallback(this);
        //        op.Successful = true;
        //        return;
        //    }

        //    Task.Delay(_delay).GetAwaiter().GetResult();
        //    op.Successful = true;
        //}
        //catch (Exception e)
        //{
        //    ExecutionException = e;

        //    // we log this here, because if the activity is not waited for later than the exception would not be logged
        //    SnTrace.WriteError(() => $"Error during security activity execution. SA{Id} {e}");
        //}

        using var op = SnTrace.StartOperation(() => $"IA: ExecuteInternal #IA{Key} (delay: {_delay})");
        Task.Delay(_delay).GetAwaiter().GetResult();
        op.Successful = true;
    }

}
