using SenseNet.Diagnostics;

namespace CentralizedIndexingActivityQueuePrototype;

public interface IIndexManager
{
    Task RegisterActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken);
    Task ExecuteActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken);

    //Task StartAsync(TextWriter consoleOut, CancellationToken cancellationToken);
    //void ShutDown();
    //int GetLastStoredIndexingActivityId();
    //IndexingActivityStatus GetCurrentIndexingActivityStatus();
    //Task<IndexingActivityStatus> LoadCurrentIndexingActivityStatusAsync(CancellationToken cancellationToken);
}

public class IndexManager : IIndexManager
{
    private readonly CentralizedIndexingActivityQueue _activityQueue;
    private readonly IDataStore _dataStore;

    public IndexManager(CentralizedIndexingActivityQueue activityQueue, IDataStore dataStore)
    {
        _activityQueue = activityQueue;
        _dataStore = dataStore;
    }


    public Task RegisterActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken)
    {
        return _dataStore.RegisterIndexingActivityAsync(activity, cancellationToken);
    }

    public Task ExecuteActivityAsync(IndexingActivityBase activity, CancellationToken cancel)
    {
        return ExecuteCentralizedActivityAsync(activity, cancel);
    }
    private async Task ExecuteCentralizedActivityAsync(IIndexingActivity activity, CancellationToken cancel)
    {
        SnTrace.Write("ExecuteCentralizedActivity: #{0}", activity.Id);
        await _activityQueue.ExecuteActivity(activity);
    }
}