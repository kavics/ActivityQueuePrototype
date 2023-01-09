namespace CentralizedIndexingActivityQueuePrototype;

public class Populator
{
    private readonly IDataStore _dataStore;
    private readonly IIndexManager _indexManager;
    private readonly IIndexingActivityFactory _indexingActivityFactory;

    public Populator(IDataStore dataStore, IIndexManager indexManager, IIndexingActivityFactory indexingActivityFactory)
    {
        _dataStore = dataStore;
        _indexManager = indexManager;
        _indexingActivityFactory = indexingActivityFactory;
    }

    internal IndexingActivityBase CreateActivity(IndexingActivityType type)
    {
        var activity = (IndexingActivityBase)_indexingActivityFactory.CreateActivity(type);
        return activity;
    }
    public Task CreateActivityAndExecuteAsync(IndexingActivityType type, CancellationToken cancel)
    {
        return ExecuteActivityAsync(CreateActivity(type), cancel);
    }

    private async Task ExecuteActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken)
    {
        await _indexManager.RegisterActivityAsync(activity, cancellationToken).ConfigureAwait(false);
        await _indexManager.ExecuteActivityAsync(activity, cancellationToken).ConfigureAwait(false);
    }
}