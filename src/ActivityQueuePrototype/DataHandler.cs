using SenseNet.Diagnostics;

namespace ActivityQueuePrototype;

public class DataHandler
{
    readonly List<Activity> _activities = new();

    public async Task SaveActivityAsync(Activity activity, CancellationToken cancel)
    {
        // Only simulation
        lock(_activities)
        {
            if (_activities.Any(a => a.Id == activity.Id))
                return;
            _activities.Add(activity);
        }
        using var op = SnTrace.StartOperation(() => $"DataHandler: SaveActivity A{activity.Key}");
        await Task.Delay(Rng.Next(20, 50), cancel).ConfigureAwait(false);
        op.Successful = true;
    }
}