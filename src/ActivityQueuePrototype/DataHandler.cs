using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

public class DataHandler
{
    readonly List<Activity> _activities = new();

    public async Task SaveActivityAsync(Activity activity, CancellationToken cancel)
    {
        if (_activities.Any(a => a.Id == activity.Id))
            return;
        using var op = SnTrace.StartOperation(() => $"DataHandler: SaveActivity A{activity.Id}");
        // ReSharper disable once SimplifyLinqExpressionUseAll
        _activities.Add(activity);
        await Task.Delay(Rng.Next(20, 50), cancel).ConfigureAwait(false);
        op.Successful = true;
    }
}