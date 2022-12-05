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
    public async Task AQ_Activities(int count)
    {
        SnTrace.Write("Test: Activity count: " + count);
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

    private string CheckTrace(List<string> trace, int count)
    {
        var t0 = Entry.Parse(trace.First()).Time;

        Dictionary<int, ActivityEvents> allEvents = new Dictionary<int, ActivityEvents>();
        foreach (var entry in trace.Select(Entry.Parse))
        {
            if (ParseLine(allEvents, entry, "App: Business executes A", "Start", out var item))
                item.BusinessStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "App: Business executes A", "End", out item))
                item.BusinessEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity A", "Start", out item))
                item.SaveStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity A", "End", out item))
                item.SaveEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "ActivityQueue: Arrive A", null, out item))
                item.Arrival = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "QueueThread: start execution: A", null, out item))
                item.Execution = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "Activity: ExecuteInternal A", "Start", out item))
                item.InternalExecutionStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "Activity: ExecuteInternal A", "End", out item))
                item.InternalExecutionEnd = entry.Time - t0;
        }

        if (allEvents.Count != count)
            return $"events.Count = {allEvents.Count}, expected: {count}";

        foreach (var events in allEvents)
        {
            if(!events.Value.IsRightOrder())
                return $"events[{events.Key}] is not in the right order";
        }

        var execTimes = allEvents.Values
            .OrderBy(x => x.Id)
            .Select(x => x.Execution)
            .ToArray();
        for (var i = 0; i < execTimes.Length - 1; i++)
        {
            if (execTimes[i] > execTimes[i+1])
                return $"execTimes[{i}] and execTimes[{i+1}] are not in the right order.";
        }

        return null;
    }

    private bool ParseLine(Dictionary<int, ActivityEvents> events, Entry entry, string msg, string? status, out ActivityEvents item)
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
    private int ParseItemId(string msg, int index)
    {
        var p = index;
        while (msg.Length > p && char.IsDigit(msg[p]))
            p++;
        var src = msg.Substring(index, p - index);
        return int.Parse(src);
    }
    private ActivityEvents EnsureItem(Dictionary<int, ActivityEvents> items, int id)
    {
        if (items.TryGetValue(id, out var item))
            return item;
        item = new ActivityEvents { Id = id };
        items.Add(id, item);
        return item;
    }


    private class ActivityEvents
    {
        public int Id;
        public TimeSpan BusinessStart;             // Start  App: Business executes A1
        public TimeSpan SaveStart;                 // Start  DataHandler: SaveActivity A1
        public TimeSpan SaveEnd;                   // End    DataHandler: SaveActivity A1
        public TimeSpan Arrival;                   //        ActivityQueue: Arrive A1
        public TimeSpan Execution;                 //        QueueThread: start execution: A1
        public TimeSpan InternalExecutionStart;    // Start  Activity: ExecuteInternal A1
        public TimeSpan InternalExecutionEnd;      // End    Activity: ExecuteInternal A1
        public TimeSpan BusinessEnd;               // End    App: Business executes A1

        public bool IsRightOrder()
        {
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