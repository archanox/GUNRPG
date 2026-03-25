namespace GUNRPG.Core.Simulation;

/// <summary>
/// Exception thrown when a node's local simulation state hash does not match the authoritative
/// state hash carried by a <see cref="SignedTick"/> or a replay validation result.
/// </summary>
public sealed class DesyncException : Exception
{
    public DesyncException(long tick, byte[] expectedHash, byte[] actualHash)
        : base($"State desync detected at tick {tick}.")
    {
        Tick = tick;
        ExpectedHash = (byte[])expectedHash.Clone();
        ActualHash = (byte[])actualHash.Clone();
    }

    /// <summary>The simulation tick at which the desync was detected.</summary>
    public long Tick { get; }

    /// <summary>The authoritative (expected) state hash from the signed tick.</summary>
    public byte[] ExpectedHash { get; }

    /// <summary>The locally computed state hash that diverged from the authority.</summary>
    public byte[] ActualHash { get; }
}
