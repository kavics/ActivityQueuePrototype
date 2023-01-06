namespace CentralizedIndexingActivityQueuePrototype;

public interface IIndexingActivityFactory
{
    IIndexingActivity CreateActivity(IndexingActivityType activityType);
}

public class IndexingActivityFactory : IIndexingActivityFactory
{
    public IIndexingActivity CreateActivity(IndexingActivityType activityType)
    {
        IIndexingActivity activity;
        switch (activityType)
        {
            case IndexingActivityType.AddDocument: activity = new AddDocumentActivity(); break;
            case IndexingActivityType.AddTree: activity = new AddTreeActivity(); break;
            case IndexingActivityType.UpdateDocument: activity = new UpdateDocumentActivity(); break;
            case IndexingActivityType.RemoveTree: activity = new RemoveTreeActivity(); break;
            case IndexingActivityType.Rebuild: activity = new RebuildActivity(); break;
            case IndexingActivityType.Restore: activity = new RestoreActivity(); break;
            default: throw new NotSupportedException("Unknown IndexingActivityType: " + activityType);
        }
        activity.ActivityType = activityType;
        return activity;
    }
}

public abstract class DocumentIndexingActivity : IndexingActivityBase
{
    protected DocumentIndexingActivity(IndexingActivityType activityType) : base(activityType) { }
}

public class AddDocumentActivity : DocumentIndexingActivity
{
    public AddDocumentActivity() : base(IndexingActivityType.AddDocument) { }
}
public class AddTreeActivity : IndexingActivityBase
{
    public AddTreeActivity() : base(IndexingActivityType.AddTree) { }
}
public class UpdateDocumentActivity : DocumentIndexingActivity
{
    public UpdateDocumentActivity() : base(IndexingActivityType.UpdateDocument) { }
}
public class RemoveTreeActivity : IndexingActivityBase
{
    public RemoveTreeActivity() : base(IndexingActivityType.RemoveTree) { }
}
public class RebuildActivity : IndexingActivityBase
{
    public RebuildActivity() : base(IndexingActivityType.Rebuild) { }
}
public class RestoreActivity : IndexingActivityBase
{
    public RestoreActivity() : base(IndexingActivityType.Restore) { }
}
