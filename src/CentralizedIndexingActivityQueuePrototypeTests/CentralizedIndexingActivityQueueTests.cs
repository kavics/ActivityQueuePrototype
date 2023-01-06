using System.Diagnostics;
using CentralizedIndexingActivityQueuePrototype;
using SenseNet.Diagnostics;
using SenseNet.Diagnostics.Analysis;

namespace CentralizedIndexingActivityQueuePrototypeTests;

[TestClass]
public class CentralizedIndexingActivityQueueTests
{
    private class TestTracer : ISnTracer
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

    [TestMethod]
    public async Task CIAQ_Lifetime()
    {
        var dataStore = new DataStore() {EnableLoad = false};
        var factory = new IndexingActivityFactory();
        var activityQueue = new CentralizedIndexingActivityQueue(dataStore, factory);
        var indexManager = new IndexManager(activityQueue, dataStore);
        var populator = new Populator(dataStore, indexManager, factory);

        try
        {
            await activityQueue.StartAsync(CancellationToken.None);

            // ACTION
            await populator.CreateActivityAndExecuteAsync(IndexingActivityType.AddDocument, CancellationToken.None);
            //await Task.Delay(10);
            //await populator.CreateActivityAndExecuteAsync(IndexingActivityType.AddDocument, CancellationToken.None);
        }
        finally
        {
            activityQueue.Dispose();
        }

        // ASSERT
        var trace = _testTracer.Lines
            .Select(Entry.Parse)
            .Select(e => e.Message)
            .Where(msg =>
            {
                if (msg.StartsWith("CIAQT: works "))
                    return false;
                if (msg.StartsWith("CIAQT: arrivalSortedList.Count: "))
                    return false;
                return true;
            })
            .ToList();
        Assert.AreEqual(trace[1], "CIAQT: started");
        Assert.AreEqual(trace[2], "CIAQT: waiting for arrival");
        Assert.AreEqual(trace[3], "ExecuteCentralizedActivity: #1");
        Assert.AreEqual(trace[4], "CIAQ: Arrive #IA1-1");
        Assert.AreEqual(trace[5], "CIAQT: start execution: #IA1-1");
        Assert.AreEqual(trace[6], "IA: ExecuteInternal #IA1-1 (delay: 0)");
        Assert.AreEqual(trace[7], "IA: ExecuteInternal #IA1-1 (delay: 0)");
        Assert.AreEqual(trace[8], "CIAQT: execution finished: #IA1-1");
        Assert.AreEqual(trace[9], "CIAQT: waiting for arrival");
        Assert.AreEqual(trace[10], "CIAQ: disposed");
    }
}