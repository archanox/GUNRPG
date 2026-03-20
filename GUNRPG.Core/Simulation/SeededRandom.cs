namespace GUNRPG.Core.Simulation;

/// <summary>
/// Seeded random implementation that carries forward compact generator state so replay resumes in O(1) time.
/// </summary>
public sealed class SeededRandom : IRandom
{
    private const ulong DefaultState = 0x9E3779B97F4A7C15UL;

    public int Seed { get; }
    public ulong State { get; private set; }
    public int CallCount { get; private set; }

    public SeededRandom(int seed)
        : this(new RngState(seed, InitializeState(seed), 0))
    {
    }

    public SeededRandom(RngState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.CallCount < 0)
        {
            throw new ArgumentOutOfRangeException($"{nameof(state)}.{nameof(state.CallCount)}");
        }

        if (state.State == 0)
        {
            throw new ArgumentException("RNG state must not be zero.", nameof(state));
        }

        Seed = state.Seed;
        State = state.State;
        CallCount = state.CallCount;
    }

    public int Next(int minValue, int maxValue)
    {
        if (minValue >= maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");
        }

        var range = (uint)(maxValue - minValue);
        var next = NextUInt32();
        return minValue + (int)(next % range);
    }

    private static ulong InitializeState(int seed)
    {
        var mixed = unchecked((ulong)(uint)seed + DefaultState);
        mixed ^= mixed >> 30;
        mixed *= 0xBF58476D1CE4E5B9UL;
        mixed ^= mixed >> 27;
        mixed *= 0x94D049BB133111EBUL;
        mixed ^= mixed >> 31;

        return mixed == 0 ? DefaultState : mixed;
    }

    private uint NextUInt32()
    {
        State ^= State >> 12;
        State ^= State << 25;
        State ^= State >> 27;
        CallCount++;

        return (uint)((State * 2685821657736338717UL) >> 32);
    }
}
