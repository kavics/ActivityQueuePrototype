﻿using SenseNet.Diagnostics;

namespace SecurityActivityQueuePrototype;

public class App : IDisposable
{
    private readonly string[] _args;
    private readonly SecurityActivityQueue _activityQueue;
    private readonly DataHandler _dataHandler;

    public App(string[] args)
    {
        _args = args;
        _dataHandler = new DataHandler();
        _activityQueue = new SecurityActivityQueue(_dataHandler);
    }

    public async Task RunAsync()
    {
        SnTrace.Write("App: start.");

        await _activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(_activityQueue);
        var cancellation = new CancellationTokenSource();

        /*
        var ids = Enumerable.Range(0, 4)
            .OrderByDescending(x => x)
            .ToArray();

        var tasks = ids
            .Select(i => Task.Run(() => ExecuteActivity(1 + i, Rng.Next(10, 50), context, cancellation.Token)))
            .ToArray();

        SnTrace.Write("App: all tasks started.");

        Task.WaitAll(tasks);

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1_000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");
        */

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // -------- random order without duplications
        foreach (var activity in new ActivityGenerator().Generate(2, 5,
                     new RngConfig(0, 50), new RngConfig(10, 50)))
        {
            tasks.Add(Task.Run(() => ExecuteActivity(activity, context, cancellation.Token)));
        }
        //// -------- random order with duplications
        //foreach (var activity in new ActivityGenerator().GenerateDuplications(2,
        //             new RngConfig(0, 50), new RngConfig(10, 50)))
        //{
        //    tasks.Add(Task.Run(() => ExecuteActivity2(activity, context, cancellation.Token)));
        //}

        await Task.WhenAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1_000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");
    }

    public static  Task ExecuteActivity(int id, int delay, Context context, CancellationToken cancel)
    {
        var activity = new SecurityActivity(id, delay);
        return ExecuteActivity(activity, context, cancel);
    }
    public static async Task ExecuteActivity(SecurityActivity activity, Context context, CancellationToken cancel)
    {
        if (activity.FromReceiver)
        {
            await activity.ExecuteAsync(context, cancel);
            return;
        }
        using var op = SnTrace.StartOperation(() => $"App: Business executes #SA{activity.Key}");
        await activity.ExecuteAsync(context, cancel);
        op.Successful = true;
    }

    public void Dispose()
    {
        _activityQueue.Dispose();
    }
}