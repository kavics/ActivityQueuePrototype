using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using ActivityQueuePrototype;
using Microsoft.Extensions.Logging;
using SenseNet.Configuration;
using SenseNet.Diagnostics;
using SenseNet.Diagnostics.Analysis;

namespace ActivityQueuePrototypeTests;

[TestClass]
public class UnitTest1
{
    private class TestTracer:ISnTracer
    {
        public List<string> Lines { get; } = new();
        public void Write(string line) { Lines.Add(line); }
        public void Flush() { /* do nothing */ }
        public void Clear() { Lines.Clear(); }
    }

    private TestTracer? _testTracer;

    public TestContext TestContext { get; set; }

    [TestInitialize]
    public void InitializeTest()
    {
        var tracers = SnTrace.SnTracers;
        _testTracer = (TestTracer)tracers.FirstOrDefault(t => t is TestTracer)!;
        if (_testTracer == null)
        {
            _testTracer = new TestTracer();
            tracers.Add(_testTracer);
        }
        else
        {
            _testTracer.Clear();
        }

        SnTrace.Custom.Enabled = true;
        SnTrace.Write("------------------------------------------------- " + TestContext.TestName);
    }


    [DataRow(1)]
    [DataRow(4)]
    [DataRow(16)]
    [DataRow(100)]
    [DataTestMethod]
    public async Task AQ_RandomActivities_WithoutDuplications(int count)
    {
        SnTrace.Write(() => "Test: Activity count: " + count);
        var dataHandler = new DataHandler();
        var activityQueue = new ActivityQueue(dataHandler);
        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // -------- random order without duplications
        foreach (var activity in new ActivityGenerator().Generate(count, 5,
                     new RngConfig(1, 1), new RngConfig(1, 1)))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, count);
        Assert.AreEqual(null, msg);
    }

    [DataRow(4)]
    //[DataRow(16)]
    //[DataRow(100)]
    [DataTestMethod]
    public async Task AQ_RandomActivities_WithDuplications(int count)
    {
        SnTrace.Write("Test: Activity count: " + count);
        var dataHandler = new DataHandler();
        var activityQueue = new ActivityQueue(dataHandler);
        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // -------- random order with duplications
        foreach (var activity in new ActivityGenerator().GenerateDuplications(count,
                     new RngConfig(0, 50), new RngConfig(10, 50)))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, count);
        Assert.AreEqual(null, msg);
    }

    private string CheckTrace(List<string> trace, int count)
    {
        var t0 = Entry.Parse(trace.First()).Time;

        var allEvents = new Dictionary<string, ActivityEvents>();
        foreach (var entry in trace.Select(Entry.Parse))
        {
            if (ParseLine(allEvents, entry, "App: Business executes ", "Start", out var item))
                item.BusinessStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "App: Business executes ", "End", out item))
                item.BusinessEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity ", "Start", out item))
                item.SaveStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity ", "End", out item))
                item.SaveEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "ActivityQueue: Arrive ", null, out item))
                item.Arrival = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "QueueThread: start execution: ", null, out item))
                item.Execution = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "Activity: ExecuteInternal ", "Start", out item))
                item.InternalExecutionStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "Activity: ExecuteInternal ", "End", out item))
                item.InternalExecutionEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "QueueThread: execution ignored ", null, out item))
                item.ExecutionIgnored = true;
        }

        var grouped = new Dictionary<int, Dictionary<int, ActivityEvents>>();
        foreach (var events in allEvents.Values)
        {
            if (!grouped.TryGetValue(events.Id, out var outer))
            {
                outer = new Dictionary<int, ActivityEvents>();
                grouped.Add(events.Id, outer);
            }
            outer.Add(events.ObjectId, events);
        }

        if (grouped.Count != count)
            return $"events.Count = {allEvents.Count}, expected: {count}";

        foreach (var events in allEvents)
        {
            if(!events.Value.IsRightOrder())
                return $"events[{events.Key}] is not in the right order";
        }

        foreach (var events in grouped.Values)
        {
            var executedCount = events.Values.Count(x => !x.ExecutionIgnored);
            var ignoredCount = events.Values.Count(x => x.ExecutionIgnored);
            var savedCount = events.Values.Count(x => x.Saved);
            var notSavedCount = events.Values.Count(x => !x.Saved);
            if (executedCount != 1)
                return $"A{events.First().Value.Id} is executed more times.";
            if (savedCount != 1)
                return $"A{events.First().Value.Id} is saved more times.";

            if (ignoredCount > 0)
            {
                // The BusinessEnd of all ignored items should greater BusinessEnd of executed item
                //     otherwise send message: "released earlier."
                
            }
        }

        var execTimes = allEvents.Values
            .Where(x => !x.ExecutionIgnored)
            .OrderBy(x => x.Id)
            .Select(x => x.Execution)
            .ToArray();
        for (var i = 0; i < execTimes.Length - 1; i++)
        {
            if (execTimes[i] > execTimes[i+1])
                return $"execTimes[{i}] and execTimes[{i+1}] are not in the right order.";
        }

        var businessEndIdsOrderedByTime = allEvents.Values
            .OrderBy(x => x.BusinessEnd)
            .Select(x => x.Id)
            .ToArray();
        for (var i = 0; i < businessEndIdsOrderedByTime.Length - 1; i++)
        {
            if (businessEndIdsOrderedByTime[i] > businessEndIdsOrderedByTime[i + 1])
                return $"businessEndIdsOrderedByTime[{i}] and businessEndIdsOrderedByTime[{i + 1}] are not in the right order.";
        }

        return null;
    }
    private bool ParseLine(Dictionary<string, ActivityEvents> events, Entry entry, string msg, string? status, out ActivityEvents item)
    {
        if(entry.Message.StartsWith(msg) && (status == null || status == entry.Status))
        {
            var id = ParseItemId(entry.Message, msg.Length);
            item = EnsureItem(events, id);
            return true;
        }
        item = null;
        return false;
    }
    private string ParseItemId(string msg, int index)
    {
        var p = index;
        while (msg.Length > p && char.IsDigit(msg[p]))
            p++;
        var src = msg.Substring(index);
        p = src.IndexOf(" ");
        if (p > 0)
            src = src.Substring(0, p);
        return src;
    }
    private ActivityEvents EnsureItem(Dictionary<string, ActivityEvents> items, string key)
    {
        if (items.TryGetValue(key, out var item))
            return item;
        var ids = key.Trim('A').Split('-').Select(int.Parse).ToArray();
        item = new ActivityEvents { Key = key, Id = ids[0], ObjectId = ids[1] };
        items.Add(key, item);
        return item;
    }

    [DebuggerDisplay("{Key}: ignored: {ExecutionIgnored}")]
    private class ActivityEvents
    {
        public int Id;
        public int ObjectId;
        public string Key;
        public TimeSpan BusinessStart;             // Start  App: Business executes A1
        public TimeSpan SaveStart;                 // Start  DataHandler: SaveActivity A1
        public TimeSpan SaveEnd;                   // End    DataHandler: SaveActivity A1
        public TimeSpan Arrival;                   //        ActivityQueue: Arrive A1
        public TimeSpan Execution;                 //        QueueThread: start execution: A1
        public TimeSpan InternalExecutionStart;    // Start  Activity: ExecuteInternal A1
        public TimeSpan InternalExecutionEnd;      // End    Activity: ExecuteInternal A1
        public TimeSpan BusinessEnd;               // End    App: Business executes A1
        public bool ExecutionIgnored;              //        QueueThread: execution ignored A3-1

        public bool Saved => SaveStart != TimeSpan.Zero;

        public bool IsRightOrder()
        {
            if (ExecutionIgnored && !Saved)
                return SaveStart == TimeSpan.Zero &&
                       SaveEnd == TimeSpan.Zero &&
                       Arrival > BusinessStart &&
                       Execution == TimeSpan.Zero &&
                       InternalExecutionStart == TimeSpan.Zero &&
                       InternalExecutionEnd == TimeSpan.Zero &&
                       BusinessEnd > Arrival;

            if (ExecutionIgnored)
                return SaveStart > BusinessStart &&
                       SaveEnd > SaveStart &&
                       Arrival > SaveEnd &&
                       Execution == TimeSpan.Zero &&
                       InternalExecutionStart == TimeSpan.Zero &&
                       InternalExecutionEnd == TimeSpan.Zero &&
                       BusinessEnd > Arrival;

            if (!Saved)
                return SaveStart == TimeSpan.Zero &&
                       SaveEnd == TimeSpan.Zero &&
                       Arrival > BusinessStart &&
                       Execution > Arrival &&
                       InternalExecutionStart > Execution &&
                       InternalExecutionEnd > InternalExecutionStart &&
                       BusinessEnd > InternalExecutionEnd;

            return SaveStart > BusinessStart &&
                   SaveEnd > SaveStart &&
                   Arrival > SaveEnd &&
                   Execution > Arrival &&
                   InternalExecutionStart > Execution &&
                   InternalExecutionEnd > InternalExecutionStart &&
                   BusinessEnd > InternalExecutionEnd;
        }
    }
}