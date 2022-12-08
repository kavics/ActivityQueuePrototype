using SenseNet.Diagnostics;
using System.Diagnostics;
using System.Threading.Channels;

namespace ActivityQueuePrototype;

public class DataHandler
{
    public bool EnableLoad { get; set; } = true;

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

    public async Task<IEnumerable<Activity>> LoadLastActivities(int fromId, CancellationToken cancel)
    {
        if (!EnableLoad)
            return Array.Empty<Activity>();

        using var op = SnTrace.StartOperation(() => $"DataHandler: LoadLastActivities(fromId: {fromId})");

        Activity[] result;
        lock (_activities)
        {
            result = _activities
                .Where(x => x.Id >= fromId)
                .Select(a => a.Clone())
                .ToArray();
        }

        await Task.Delay(Rng.Next(20, 50), cancel).ConfigureAwait(false);
        op.Successful = true;
        return result;
    }
}