namespace GUNRPG.Core.Simulation;

/// <summary>
/// Seeded random implementation that can be reconstructed from seed + call count for replay.
/// Rebuilding the generator fast-forwards by replaying prior calls, which is acceptable for the
/// small deterministic combat traces used here and keeps the persisted RNG state minimal.
/// </summary>
public sealed class SeededRandom : IRandom
{
    private readonly Random _random;

    public int Seed { get; }
    public int CallCount { get; private set; }

    public SeededRandom(int seed, int callCount = 0)
    {
        if (callCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(callCount));
        }

        Seed = seed;
        _random = new Random(seed);

        for (var i = 0; i < callCount; i++)
        {
            _random.Next();
        }

        CallCount = callCount;
    }

    public int Next(int minValue, int maxValue)
    {
        CallCount++;
        return _random.Next(minValue, maxValue);
    }
}
