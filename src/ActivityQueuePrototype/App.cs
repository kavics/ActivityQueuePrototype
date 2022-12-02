using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

internal class App : IDisposable
{
    private readonly string[] _args;
    private readonly ActivityQueue _activityQueue;
    private readonly DataHandler _dataHandler;

    public App(string[] args)
    {
        _args = args;
        _dataHandler = new DataHandler();
        _activityQueue = new ActivityQueue(_dataHandler);
    }

    public async Task RunAsync()
    {
        SnTrace.Write("App start.");

        var context = new Context(_activityQueue);
        var cancellation = new CancellationTokenSource();
        
        var tasks = Enumerable.Range(0, 4)
            .Select(i => Task.Run(() => ExecuteActivity(1 + i, Rng.Next(10, 50), context, cancellation.Token)))
            .ToArray();

        SnTrace.Write("App: all tasks started.");

        Task.WaitAll(tasks);

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(2_000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");
    }

    private List<Activity> _activities = new();
    public async Task ExecuteActivity(int id, int delay, Context context, CancellationToken cancel)
    {
        var activity = new Activity(id, delay);
        _activities.Add(activity);
        using var op = SnTrace.StartOperation(() => $"App: Business executes A{activity.Id}");
        await activity.ExecuteAsync(context, cancel);
        op.Successful = true;
    }

    public void Dispose()
    {
        _activityQueue.Dispose();
    }
}