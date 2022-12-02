namespace ActivityQueuePrototype;

internal class Rng
{
    private static Random _random = new Random();

    public static int Next(int min, int max)
    {
        return _random.Next(min, max);
    }
}