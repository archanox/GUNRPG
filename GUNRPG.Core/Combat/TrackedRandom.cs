namespace GUNRPG.Core.Combat;

/// <summary>
/// Random wrapper that tracks call count so deterministic state can be restored.
/// </summary>
public sealed class TrackedRandom : Random
{
    public int Seed { get; }
    public int CallCount { get; private set; }

    public TrackedRandom(int seed, int callCount = 0) : base(seed)
    {
        Seed = seed;
        if (callCount > 0)
        {
            Advance(callCount);
        }
    }

    private void Advance(int calls)
    {
        for (int i = 0; i < calls; i++)
        {
            base.Next();
        }

        CallCount += calls;
    }

    public override int Next()
    {
        CallCount++;
        return base.Next();
    }

    public override int Next(int maxValue)
    {
        CallCount++;
        return base.Next(maxValue);
    }

    public override int Next(int minValue, int maxValue)
    {
        CallCount++;
        return base.Next(minValue, maxValue);
    }

    public override double NextDouble()
    {
        CallCount++;
        return base.NextDouble();
    }

    public override void NextBytes(byte[] buffer)
    {
        CallCount++;
        base.NextBytes(buffer);
    }

    public override void NextBytes(Span<byte> buffer)
    {
        CallCount++;
        base.NextBytes(buffer);
    }
}
