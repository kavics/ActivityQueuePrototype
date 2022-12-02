namespace ActivityQueuePrototype;

internal class DataHandler
{
    public Task SaveActivityAsync(Activity activity, CancellationToken cancel)
    {
        return Task.CompletedTask;
    }
}