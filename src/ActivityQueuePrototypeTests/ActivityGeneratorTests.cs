using ActivityQueuePrototype;

namespace ActivityQueuePrototypeTests;

[TestClass]
public class ActivityGeneratorTests
{
    [TestMethod]
    public void Generator_GenerateByIds()
    {
        var ids = new ActivityGenerator().GenerateByIds(new[] { 3, 2, 1, 2, 3 },
                new RngConfig(0, 0), new RngConfig(10, 20))
            .Select(x => x.Id.ToString());

        Assert.AreEqual("3, 2, 1, 2, 3", string.Join(", ", ids));
    }
    [TestMethod]
    public void Generator_Generate()
    {
        var count = 10;

            // Action
            var ids = new ActivityGenerator().Generate(count, 5,
                    new RngConfig(0, 0), new RngConfig(10, 20))
                .Select(x => x.Id).ToArray();

            // Assert
            Assert.AreEqual(count, ids.Length);
            Assert.AreEqual(count, ids.Distinct().Count());

            var q = 0;
            for (var i = 0; i < count; i++)
                q += Math.Abs(i - ids[i]);
            Assert.IsTrue(q > 2);
    }



    [TestMethod]
    public void G_One()
    {
        // Arrange
        var expected = 42;

        // Action
        var activity = new ActivityGenerator().GenerateOne(expected);

        // Assert
        Assert.AreEqual(expected, activity.Id);
    }
    [TestMethod]
    public void G_1()
    {
        // Arrange
        var expected = 42;

        // Action
        var activity = new ActivityGenerator().GenerateOne(expected);

        // Assert
        Assert.AreEqual(expected, activity.Id);
    }
    [TestMethod]
    public void G_2()
    {
        // Arrange
        var expected = 142;

        // Action
        var activity = new ActivityGenerator().GenerateOne(expected);

        // Assert
        Assert.AreEqual(expected, activity.Id);
    }
}