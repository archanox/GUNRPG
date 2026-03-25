namespace GUNRPG.Core.Simulation;

/// <summary>
/// Exception thrown when a <see cref="SignedTick"/> carries a cryptographically invalid signature.
/// Receiving an invalid signature indicates either tampering or an incorrect authority key.
/// </summary>
public sealed class InvalidSignatureException : Exception
{
    public InvalidSignatureException(long tick)
        : base($"Invalid authority signature at tick {tick}.")
    {
        Tick = tick;
    }

    /// <summary>The simulation tick whose signature failed verification.</summary>
    public long Tick { get; }
}
