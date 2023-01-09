namespace SecurityActivityQueuePrototype;

public class Context
{
    public SecurityActivityQueue ActivityQueue { get; }

    public Context(SecurityActivityQueue activityQueue)
    {
        ActivityQueue = activityQueue;
    }
}