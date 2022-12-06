using System.Collections;

namespace ActivityQueuePrototype;

public class ActivityGenerator
{
    public IEnumerable<Activity> Generate(int count, int randomness, RngConfig creationDelay, RngConfig executionDelay)
    {
        return GenerateByIds(GenerateIds(count, randomness), creationDelay, executionDelay);
    }
    public IEnumerable<Activity> GenerateDuplications(int maxId, RngConfig creationDelay, RngConfig executionDelay)
    {
        return GenerateByIds(GenerateRandomIds(maxId), creationDelay, executionDelay);
    }
    public IEnumerable<Activity> GenerateByIds(IEnumerable<int> ids, RngConfig creationDelay, RngConfig executionDelay)
    {
        foreach (var id in ids)
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

    public IEnumerable<int> GenerateRandomIds(int maxId)
    {
        var missingIds = new List<int>(Enumerable.Range(1, maxId));
        var result = new List<int>();
        for (int i = 0; i < maxId * 2; i++)
        {
            var id = Rng.Next(1, maxId + 1);
            result.Add(id);
            missingIds.Remove(id);
            if (missingIds.Count == 0)
                break;
        }
        result.AddRange(missingIds);
        return result;
    }

    public Activity GenerateOne(int id)
    {
        return new Activity(id, 0);
    }
}
