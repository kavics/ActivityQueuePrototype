namespace CentralizedIndexingActivityQueuePrototype;

public class ExecutableIndexingActivitiesResult
{
    public IIndexingActivity[] Activities { get; set; }
    public int[] FinishedActivitiyIds { get; set; }
}

public interface IDataStore
{
    Task RegisterIndexingActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken);

    public Task<ExecutableIndexingActivitiesResult> LoadExecutableIndexingActivitiesAsync(
        IIndexingActivityFactory activityFactory, int maxCount, int runningTimeoutInSeconds,
        int[] waitingActivityIds,
        CancellationToken cancellationToken);

}

public class IndexingActivityDoc
{
    public int Id => IndexingActivityId;

    public int IndexingActivityId;
    public IndexingActivityType ActivityType;
    public DateTime CreationDate;
    public IndexingActivityRunningState RunningState;
    public DateTime? LockTime;
    public int NodeId;
    public int VersionId;
    public string Path;
    public string Extension;

    public IndexingActivityDoc Clone()
    {
        return new IndexingActivityDoc
        {
            IndexingActivityId = IndexingActivityId,
            ActivityType = ActivityType,
            CreationDate = CreationDate,
            RunningState = RunningState,
            LockTime = LockTime,
            NodeId = NodeId,
            VersionId = VersionId,
            Path = Path,
            Extension = Extension,
        };
    }
}

public class DataStore : IDataStore
{
    /*
    DATAHANDLER ALGORITHMS
    ======================

    DataStore.RefreshIndexingActivityLockTimeAsync(waitingIds, CancellationToken.None)

    DECLARE @IdTable AS TABLE(Id INT)
    INSERT INTO @IdTable SELECT CONVERT(int, [value]) FROM STRING_SPLIT(@Ids, ',');
    UPDATE IndexingActivities SET LockTime = @LockTime WHERE IndexingActivityId IN (SELECT Id FROM @IdTable)

    public override STT.Task RefreshIndexingActivityLockTimeAsync(int[] waitingIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (DB.IndexingActivities)
        {
            var now = DateTime.UtcNow;
            foreach (var waitingId in waitingIds)
            {
                var activity = DB.IndexingActivities.FirstOrDefault(r => r.IndexingActivityId == waitingId);
                if (activity != null)
                    activity.LockTime = now;
            }
        }
        return STT.Task.CompletedTask;
    }

    -------------------------------------------------------------------------------

    DataStore.DeleteFinishedIndexingActivitiesAsync(CancellationToken.None)

    DELETE FROM IndexingActivities WHERE RunningState = 'Done' AND (LockTime < DATEADD(MINUTE, -23, GETUTCDATE()) OR LockTime IS NULL)

    public override STT.Task DeleteFinishedIndexingActivitiesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (DB.IndexingActivities)
            foreach(var existing in DB.IndexingActivities.Where(x => x.RunningState == IndexingActivityRunningState.Done).ToArray())
                DB.IndexingActivities.Remove(existing);
        return STT.Task.CompletedTask;
    }

    -------------------------------------------------------------------------------

    DataStore.UpdateIndexingActivityRunningStateAsync(act.Id, IndexingActivityRunningState.Done, CancellationToken.None)

    UPDATE IndexingActivities SET RunningState = @RunningState, LockTime = GETUTCDATE() WHERE IndexingActivityId = @IndexingActivityId

    public override STT.Task UpdateIndexingActivityRunningStateAsync(int indexingActivityId, IndexingActivityRunningState runningState, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (DB.IndexingActivities)
        {
            var activity = DB.IndexingActivities.FirstOrDefault(r => r.IndexingActivityId == indexingActivityId);
            if (activity != null)
                activity.RunningState = runningState;
        }
        return STT.Task.CompletedTask;
    }

    -------------------------------------------------------------------------------

    var result = DataStore.LoadExecutableIndexingActivitiesAsync(indexingActivityFactory, axCount, ningTimeoutInSeconds, waitingActivityIds, CancellationToken.None)

    UPDATE IndexingActivities WITH (TABLOCK) SET RunningState = 'Running', LockTime = GETUTCDATE()
    OUTPUT INSERTED.IndexingActivityId, INSERTED.ActivityType, INSERTED.CreationDate, INSERTED.RunningState, INSERTED.LockTime,
        INSERTED.NodeId, INSERTED.VersionId, INSERTED.Path, INSERTED.Extension,
        V.IndexDocument, N.NodeTypeId, N.ParentNodeId, N.IsSystem,
        N.LastMinorVersionId, N.LastMajorVersionId, V.Status, N.Timestamp NodeTimestamp, V.Timestamp VersionTimestamp
        FROM IndexingActivities I
            LEFT OUTER JOIN Versions V ON V.VersionId = I.VersionId
            LEFT OUTER JOIN Nodes N on N.NodeId = I.NodeId
    WHERE IndexingActivityId IN (
        SELECT TOP (@Top) NEW.IndexingActivityId FROM IndexingActivities NEW
        WHERE 
            (NEW.RunningState = 'Waiting' OR ((NEW.RunningState = 'Running' AND NEW.LockTime < @TimeLimit))) AND
            NOT EXISTS (
                SELECT IndexingActivityId FROM IndexingActivities OLD
                WHERE (OLD.IndexingActivityId < NEW.IndexingActivityId) AND (
                    (OLD.RunningState = 'Waiting' OR OLD.RunningState = 'Running') AND (
                        NEW.NodeId = OLD.NodeId OR
                        (NEW.VersionId != 0 AND NEW.VersionId = OLD.VersionId) OR
                        NEW.[Path] LIKE OLD.[Path] + '/%' OR
                        OLD.[Path] LIKE NEW.[Path] + '/%'
                    )
                )
            )
        ORDER BY NEW.IndexingActivityId
    )

    Additional script if "waitingActivityIds" is not null and not empty

    -- Load set of finished activity ids.
    DECLARE @IdTable AS TABLE(Id INT)
    INSERT INTO @IdTable SELECT CONVERT(int, [value]) FROM STRING_SPLIT(@WaitingIds, ',');
    SELECT IndexingActivityId FROM IndexingActivities
    WHERE RunningState = 'Done' AND IndexingActivityId IN (SELECT Id FROM @IdTable)

    public override Task<ExecutableIndexingActivitiesResult> LoadExecutableIndexingActivitiesAsync(IIndexingActivityFactory activityFactory,
        int maxCount, int runningTimeoutInSeconds, int[] waitingActivityIds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var skipFinished = waitingActivityIds == null || waitingActivityIds.Length == 0;
        var activities = LoadExecutableIndexingActivities(activityFactory, maxCount, runningTimeoutInSeconds, cancellationToken);
        lock (DB.IndexingActivities)
        {
            var finishedActivityIds = skipFinished
                ? DB.IndexingActivities
                    .Where(x => x.RunningState == IndexingActivityRunningState.Done)
                    .Select(x => x.IndexingActivityId)
                    .ToArray()
                : DB.IndexingActivities
                    .Where(x => waitingActivityIds.Contains(x.IndexingActivityId) &&
                                x.RunningState == IndexingActivityRunningState.Done)
                    .Select(x => x.IndexingActivityId)
                    .ToArray();
            return STT.Task.FromResult(new ExecutableIndexingActivitiesResult
            {
                Activities = activities,
                FinishedActivitiyIds = skipFinished ? new int[0] : finishedActivityIds
            });
        }
    }

    ================================================================================================================

    */

    private static int _lastId = 0;
    private readonly List<IndexingActivityDoc> _indexingActivities = new();

    public bool EnableLoad { get; set; } = true; // Only for the prototype

    public Task RegisterIndexingActivityAsync(IndexingActivityBase activity, CancellationToken cancellationToken)
    {
        var newId = Interlocked.Increment(ref _lastId);
        activity.Id = newId;
        _indexingActivities.Add(new IndexingActivityDoc
        {
            IndexingActivityId = newId,
            ActivityType = activity.ActivityType,
            CreationDate = DateTime.UtcNow,
            RunningState = activity.RunningState,
            LockTime = activity.LockTime,
            //NodeId = activity.NodeId,
            //VersionId = activity.VersionId,
            //Path = activity.Path,
            //Extension = activity.Extension
        });
        return Task.CompletedTask;
    }

    public Task<ExecutableIndexingActivitiesResult> LoadExecutableIndexingActivitiesAsync(IIndexingActivityFactory activityFactory,
        int maxCount, int runningTimeoutInSeconds, int[] waitingActivityIds, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}