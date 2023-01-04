using System.Diagnostics;
using SecurityActivityQueuePrototype;
using SenseNet.Diagnostics;
using SenseNet.Diagnostics.Analysis;
using SecurityActivity = SecurityActivityQueuePrototype.SecurityActivity;

namespace SecurityActivityQueuePrototypeTests;

[TestClass]
public class SecurityActivityQueueTests
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
            tracers.Add(new SnFileSystemTracer());
        }
        else
        {
            _testTracer.Clear();
        }

        SnTrace.Custom.Enabled = true;
        SnTrace.Write("------------------------------------------------- " + TestContext.TestName);
    }
    [TestCleanup]
    public void CleanupTest()
    {
        SnTrace.Flush();
    }


    [DataRow(1)]
    [DataRow(4)]
    [DataRow(16)]
    [DataRow(100)]
    [DataTestMethod]
    public async Task AQ_RandomActivities_WithoutDuplications(int count)
    {
        SnTrace.Write(() => "Test: Activity count: " + count);
        var dataHandler = new DataHandler {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

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
    [DataRow(16)]
    [DataRow(100)]
    [DataTestMethod]
    public async Task AQ_RandomActivities_WithDuplications(int count)
    {
        SnTrace.Write("Test: Activity count: " + count);
        var dataHandler = new DataHandler {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // -------- random order with duplications
        foreach (var activity in new ActivityGenerator().GenerateDuplications(count,
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

    [TestMethod]
    public async Task AQ_Activities_Duplications()
    {
        var dataHandler = new DataHandler {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // -------- random order with duplications
        foreach (var activity in new ActivityGenerator().GenerateByIds(new[] {1, 1, 1},
                     new RngConfig(0, 0), new RngConfig(50, 50)))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 1);
        Assert.AreEqual(null, msg);
    }

    [TestMethod]
    public async Task AQ_Activities_Dependencies_1()
    {
        var dataHandler = new DataHandler {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // one dependency
        foreach (var activity in new ActivityGenerator().GenerateByIds(new[] { 1, 2 },
                     new RngConfig(0, 0), new RngConfig(50, 50),
                     (newer, older) => newer.Id % 2 == 0 && older.Id == newer.Id - 1))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 2);
        Assert.AreEqual(null, msg);
    }
    [TestMethod]
    public async Task AQ_Activities_Dependencies_Chain()
    {
        var dataHandler = new DataHandler {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 4 chain 4 length dependency chains
        var ids = Enumerable.Range(1, 16).OrderByDescending(x=>x).ToArray();
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id % 4 != 1 && older.Id == newer.Id - 1))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, ids.Length);
        Assert.AreEqual(null, msg);
    }
    [TestMethod]
    public async Task AQ_Activities_Dependencies_ParallelsAndChains()
    {
        var dataHandler = new DataHandler{ EnableLoad = false };
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 8 activities but 1 dependency chain
        var ids = Enumerable.Range(1, 8).OrderByDescending(x => x).ToArray();
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id >= 4 && newer.Id <= 6 && older.Id == newer.Id - 1))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, ids.Length);
        Assert.AreEqual(null, msg);
    }
    [TestMethod]
    public async Task AQ_Activities_Dependencies_ParallelsAndChainsAndAttachments()
    {
        var dataHandler = new DataHandler{ EnableLoad = false };
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 8 activities but 1 dependency chain
        var ids = new[] {8, 7, 6, 5, 4, 3, 2, 8, 7, 6, 5, 4, 3, 2, 1};
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id >= 4 && newer.Id <= 6 && older.Id == newer.Id - 1))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 8);
        Assert.AreEqual(null, msg);
    }

    [TestMethod]
    public async Task AQ_Activities_DB_Dependencies_ParallelsAndChainsAndAttachments()
    {
        var dataHandler = new DataHandler();
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 8 activities but 1 dependency chain
        var ids = new[] { 8, 7, 6, 5, 4, 3, 2, 8, 7, 6, 5, 4, 3, 2, 1 };
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id >= 4 && newer.Id <= 6 && older.Id == newer.Id - 1))
        {
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 8);
        Assert.AreEqual(null, msg);
    }

    [TestMethod]
    public async Task AQ_Activities_Net_Dependencies_ParallelsAndChainsAndAttachments()
    {
        var dataHandler = new DataHandler { EnableLoad = false };
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 8 activities but 1 dependency chain
        var ids = new[] { 8, 7, 6, 5, 4, 3, 2, 8, 7, 6, 5, 4, 3, 2, 1 };
        var index = 0;
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id >= 4 && newer.Id <= 6 && older.Id == newer.Id - 1))
        {
            activity.FromReceiver = index++ % 2 != 0;
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 8);
        Assert.AreEqual(null, msg);
    }

    [TestMethod]
    public async Task AQ_Activities_DbNet_Dependencies_ParallelsAndChainsAndAttachments()
    {
        var dataHandler = new DataHandler();
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        var tasks = new List<Task>();
        SnTrace.Write("App: activity generator started.");

        // 8 activities but 1 dependency chain
        var ids = new[] { 8, 7, 6, 5, 4, 3, 2, 8, 7, 6, 5, 4, 3, 2, 1 };
        var index = 0;
        foreach (var activity in new ActivityGenerator().GenerateByIds(ids,
                     new RngConfig(1, 1), new RngConfig(50, 50),
                     (newer, older) => newer.Id >= 4 && newer.Id <= 6 && older.Id == newer.Id - 1))
        {
            activity.FromReceiver = index++ % 2 != 0;
            tasks.Add(Task.Run(() => App.ExecuteActivity(activity, context, cancellation.Token)));
        }

        Task.WaitAll(tasks.ToArray());

        SnTrace.Write("App: wait for all activities finalization.");
        await Task.Delay(1000).ConfigureAwait(false);
        SnTrace.Write("App: finished.");

        var trace = _testTracer.Lines;
        var msg = CheckTrace(trace, 8);
        Assert.AreEqual(null, msg);
    }

    [TestMethod]
    public async Task AQ_Gaps_Start()
    {
        var dataHandler = new DataHandler();
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(new CompletionState{LastActivityId = 10, Gaps = new []{4, 6, 7}}, 15,
            CancellationToken.None);

        // ACTION
        var state = activityQueue.GetCompletionState();

        // ASSERT
        Assert.AreEqual("10(4,6,7)", state.ToString());
    }
    [TestMethod]
    public async Task AQ_Gaps_Loaded()
    {
        var dataHandler = new DataHandler();
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await Task.WhenAll(Enumerable.Range(1, 15)
            .Select(i => dataHandler.SaveActivityAsync(new Activity(1, 10), CancellationToken.None))
        );

        // ACTION
        await activityQueue.StartAsync(new CompletionState { LastActivityId = 10, Gaps = new[] { 4, 6, 7 } }, 15,
            CancellationToken.None);
        await Task.Delay(100);

        // ASSERT
        var state = activityQueue.GetCompletionState();
        Assert.AreEqual("15()", state.ToString());
    }
    [TestMethod]
    public async Task AQ_Gaps_Working_123()
    {
        void ExecutionCallback(SecurityActivity obj) { throw new NotImplementedException(); }

        var dataHandler = new DataHandler();
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();

        await App.ExecuteActivity(new Activity(1, 0), context, CancellationToken.None);
        await Task.Delay(1);
        var state = activityQueue.GetCompletionState();
        Assert.AreEqual("1()", state.ToString());
        
        await App.ExecuteActivity(new Activity(2, 0, null, ExecutionCallback), context, CancellationToken.None);
        await Task.Delay(1);
        state = activityQueue.GetCompletionState();
        Assert.AreEqual("1()", state.ToString());

        await App.ExecuteActivity(new Activity(3, 0), context, CancellationToken.None);
        await Task.Delay(1);
        state = activityQueue.GetCompletionState();
        Assert.AreEqual("3(2)", state.ToString());
    }
    [TestMethod]
    public async Task AQ_Gaps_Working_132()
    {
        void ExecutionCallback(SecurityActivity obj) { throw new NotSupportedException(); }

        var dataHandler = new DataHandler() {EnableLoad = false};
        var activityQueue = new SecurityActivityQueue(dataHandler);
        await activityQueue.StartAsync(CancellationToken.None);

        var context = new Context(activityQueue);
        var cancellation = new CancellationTokenSource();
        var cancel = cancellation.Token;

        var tasks = new[]
        {
            App.ExecuteActivity(new Activity(1, 0), context, cancel),
            App.ExecuteActivity(new Activity(3, 0), context, cancel),
            App.ExecuteActivity(new Activity(2, 0, null, ExecutionCallback), context, cancel)
        };
        await Task.WhenAll(tasks);
        await Task.Delay(100, cancel);
        var state = activityQueue.GetCompletionState();
        Assert.AreEqual("3(2)", state.ToString());
    }


    private string CheckTrace(List<string> trace, int count)
    {
        var t0 = Entry.Parse(trace.First()).Time;

        var allEvents = new Dictionary<string, ActivityEvents>();
        var allEntries = trace.Select(Entry.Parse);
        foreach (var entry in allEntries)
        {
            // SAQT: execution ignored immediately: A1-1
            // SAQT: execution finished: A1-1
            // SAQT: execution ignored (attachment): A1-1

            if (ParseLine(allEvents, entry, "App: Business executes ", "Start", out var item))
                item.BusinessStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "App: Business executes ", "End", out item))
                item.BusinessEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity ", "Start", out item))
                item.SaveStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "DataHandler: SaveActivity ", "End", out item))
                item.SaveEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SAQ: Arrive from receiver ", null, out item))
            { item.Arrival = entry.Time - t0; item.FromDbOrReceiver = true; }
            else if (ParseLine(allEvents, entry, "SAQ: Arrive from database ", null, out item))
            { item.Arrival = entry.Time - t0; item.FromDbOrReceiver = true; }
            else if (ParseLine(allEvents, entry, "SAQ: Arrive ", null, out item))
                item.Arrival = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SAQT: start execution: ", null, out item))
                item.Execution = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SA: ExecuteInternal ", "Start", out item))
                item.InternalExecutionStart = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SA: ExecuteInternal ", "End", out item))
                item.InternalExecutionEnd = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SAQT: execution finished: ", null, out item))
                item.Released = entry.Time - t0;
            else if (ParseLine(allEvents, entry, "SAQT: execution ignored immediately: ", null, out item))
            {   item.ExecutionIgnored = true; item.Released = entry.Time - t0; }
            else if (ParseLine(allEvents, entry, "SAQT: execution ignored (attachment): ", null, out item))
            {   item.ExecutionIgnored = true; item.Released = entry.Time - t0; }
        }

        foreach (var entry in allEntries)
        {
            if (ParseLine(allEvents, entry, "SA: Make dependency: ", null, out var item))
                item.DependsFrom.Add(ParseDependsFrom(allEvents, entry.Message));
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
                var executed = events.Values
                    .First(x => !x.ExecutionIgnored);
                var ignored = events.Values
                    .Where(x => x.ExecutionIgnored)
                    .ToArray();
                foreach (var item in ignored)
                    if(item.Released <= executed.Released)
                        return $"A{item.Id} is executed too early.";
            }
        }



        var allExecuted = allEvents.Values
            .Where(x => !x.ExecutionIgnored)
            .OrderBy(x => x.Id)
            .ToArray();
        for (var i = 0; i < allExecuted.Length; i++)
        {
            if (allExecuted[i].DependsFrom.Count > 0)
            {
                foreach (var itemBefore in allExecuted[i].DependsFrom)
                {
                    if (allExecuted[i].InternalExecutionStart < itemBefore.InternalExecutionEnd)
                        return $"The pending item A{allExecuted[i].Id} was started earlier " +
                               $"than A{itemBefore.Id} would have been completed.";
                }
                continue;
            }

            if (i < allExecuted.Length - 1)
                if (allExecuted[i].Execution > allExecuted[i + 1].Execution)
                    return $"execTimes[A{allExecuted[i].Id}] and execTimes[A{allExecuted[i + 1].Id}] are not in the right order.";
        }

        //var businessEndIdsOrderedByTime = allEvents.Values
        //    .Where(x => !x.ExecutionIgnored)
        //    .OrderBy(x => x.BusinessEnd)
        //    .Select(x => x.Id)
        //    .ToArray();
        //for (var i = 0; i < businessEndIdsOrderedByTime.Length - 1; i++)
        //{
        //    if (businessEndIdsOrderedByTime[i] > businessEndIdsOrderedByTime[i + 1])
        //        return $"businessEndIdsOrderedByTime[{i}] and businessEndIdsOrderedByTime[{i + 1}] are not in the right order.";
        //}

        return null;
    }

    private char[] _trimChars = "#SA".ToCharArray();
    private ActivityEvents ParseDependsFrom(Dictionary<string, ActivityEvents> allEvents, string msg)
    {
        // SA: Make dependency: #SA4-5 depends from SA3-6.
        var p = msg.IndexOf("depends from ", StringComparison.Ordinal);
        var key = "#SA" + msg.Substring(p + 13).TrimStart(_trimChars).TrimEnd('.');
        return allEvents[key];
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
        var src = msg.Substring(index);
        var p = src.IndexOf(" ");
        if (p > 0)
            src = src.Substring(0, p);
        return src;
    }
    private ActivityEvents EnsureItem(Dictionary<string, ActivityEvents> items, string key)
    {
        if (items.TryGetValue(key, out var item))
            return item;
        try
        {
            var ids = key.Trim(_trimChars).Split('-').Select(int.Parse).ToArray();
            item = new ActivityEvents { Key = key, Id = ids[0], ObjectId = ids[1] };
        }
        catch (Exception e)
        {
            throw;
        }
        items.Add(key, item);
        return item;
    }

    [DebuggerDisplay("{Key}: ignored: {ExecutionIgnored}")]
    private class ActivityEvents
    {
        public int Id;
        public int ObjectId;
        public string Key;
        public bool FromDbOrReceiver;                     // 
        public TimeSpan BusinessStart;                    // Start  App: Business executes #SA1
        public TimeSpan SaveStart;                        // Start  DataHandler: SaveActivity #SA1
        public TimeSpan SaveEnd;                          // End    DataHandler: SaveActivity #SA1
        public TimeSpan Arrival;                          //        SAQ: Arrive #SA1
        public TimeSpan Execution;                        //        SAQT: start execution: #SA1
        public TimeSpan InternalExecutionStart;           // Start  SA: ExecuteInternal #SA1
        public TimeSpan InternalExecutionEnd;             // End    SA: ExecuteInternal #SA1
        public TimeSpan Released;                         //        SAQT: execution ignored immediately: #SA1-1
                                                          //        SAQT: execution finished: #SA1-1
                                                          //        SAQT: execution ignored (attachment): #SA1-1
        public TimeSpan BusinessEnd;                      // End    App: Business executes #SA1
        public bool ExecutionIgnored;                     //        SAQT: execution ignored #SA3-1
        public List<ActivityEvents> DependsFrom = new();  //        SA: Make dependency: #SA4-5 depends from SA3-6.

        public bool Saved => SaveStart != TimeSpan.Zero;

        public bool IsRightOrder()
        {
            if (FromDbOrReceiver && ExecutionIgnored)
                return SaveStart == TimeSpan.Zero &&
                       SaveEnd == TimeSpan.Zero &&
                       BusinessStart == TimeSpan.Zero &&
                       Arrival > TimeSpan.Zero &&
                       Execution == TimeSpan.Zero &&
                       InternalExecutionStart == TimeSpan.Zero &&
                       InternalExecutionEnd == TimeSpan.Zero &&
                       Released > Arrival &&
                       BusinessEnd == TimeSpan.Zero;

            if (FromDbOrReceiver && !ExecutionIgnored)
                return SaveStart == TimeSpan.Zero &&
                       SaveEnd == TimeSpan.Zero &&
                       BusinessStart == TimeSpan.Zero &&
                       Arrival > TimeSpan.Zero &&
                       Execution > Arrival &&
                       InternalExecutionStart > Execution &&
                       InternalExecutionEnd > InternalExecutionStart &&
                       Released > InternalExecutionEnd &&
                       BusinessEnd == TimeSpan.Zero;

            if (ExecutionIgnored && !Saved)
                return SaveStart == TimeSpan.Zero &&
                       SaveEnd == TimeSpan.Zero &&
                       Arrival > BusinessStart &&
                       Execution == TimeSpan.Zero &&
                       InternalExecutionStart == TimeSpan.Zero &&
                       InternalExecutionEnd == TimeSpan.Zero &&
                       Released > Arrival &&
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