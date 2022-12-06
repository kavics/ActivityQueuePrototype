namespace ActivityQueuePrototype;

public class Context
{
    public ActivityQueue ActivityQueue { get; }

    public Context(ActivityQueue activityQueue)
    {
        ActivityQueue = activityQueue;
    }
}