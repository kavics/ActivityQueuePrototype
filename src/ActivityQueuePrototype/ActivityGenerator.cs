namespace ActivityQueuePrototype;

internal class ActivityGenerator
{
    public IEnumerable<Activity> Generate(int count, int randomness, RngConfig creationDelay, RngConfig executionDelay)
    {
        foreach (var id in GenerateIds(count, randomness))
        {
            var delay = Rng.Next(creationDelay.Min, creationDelay.Max);
            if (delay > 0)
                Task.Delay(delay).Wait();
            yield return new Activity(id, Rng.Next(executionDelay.Min, executionDelay.Max));
        }
    }
    public IEnumerable<int> GenerateIds(int count, int randomness)
    {
        var ids = Enumerable.Range(1, count).ToArray();
        for (int repeat = 0; repeat < 3; repeat++)
        {
            for (int i = 0; i < count - 1; i++)
            {
                var i1 = Math.Min(count - 1, i + Rng.Next(0, randomness + 1));
                if (Math.Abs(ids[i] - 1 - i1) < randomness && Math.Abs(ids[i1] - 1 - i) < randomness)
                    (ids[i], ids[i1]) = (ids[i1], ids[i]);
            }
        }
        return ids;
    }
}
