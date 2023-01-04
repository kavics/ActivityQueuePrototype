using SenseNet.Diagnostics;

namespace SecurityActivityQueuePrototype;

/// <summary>
/// Contains information about the executed activities and last activity id in the database.
/// </summary>
public class LoadCompletionStateResult
{
    /// <summary>
    /// Gets or sets the current CompletionState containing information about the executed activities.
    /// </summary>
    public CompletionState CompletionState { get; set; }
    /// <summary>
    /// Gets or sets the last executed activity id in the database.
    /// </summary>
    public int LastDatabaseId { get; set; }
}

public class DataHandler
{
    public bool EnableLoad { get; set; } = true;

    readonly List<SecurityActivity> _activities = new();

    public async Task SaveActivityAsync(SecurityActivity activity, CancellationToken cancel)
    {
        // Only simulation
        lock(_activities)
        {
            if (_activities.Any(a => a.Id == activity.Id))
                return;
            _activities.Add(activity);
        }
        using var op = SnTrace.StartOperation(() => $"DataHandler: SaveActivity #SA{activity.Key}");
        await Task.Delay(Rng.Next(20, 50), cancel).ConfigureAwait(false);
        op.Successful = true;
    }

    public async Task<IEnumerable<SecurityActivity>> LoadLastActivities(int fromId, CancellationToken cancel)
    {
        if (!EnableLoad)
            return Array.Empty<SecurityActivity>();

        using var op = SnTrace.StartOperation(() => $"DataHandler: LoadLastActivities(fromId: {fromId})");

        SecurityActivity[] result;
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

    internal Task<LoadCompletionStateResult> LoadCompletionStateAsync(CancellationToken cancel)
    {
        //UNDONE: LoadCompletionStateAsync is not implemented;
        var result = new LoadCompletionStateResult {CompletionState = new CompletionState(), LastDatabaseId = 0};
        return Task.FromResult(result);
    }

}