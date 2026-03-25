using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Abstraction for an authority node that can sign and verify per-tick simulation state.
/// Extends <see cref="ITickVerifier"/> with signing capability, meaning implementations
/// require access to the authority's private key.
/// Implemented by <see cref="SessionAuthority"/> and can be mocked in tests.
/// Validator and client nodes that only need to verify ticks should depend on
/// <see cref="ITickVerifier"/> instead.
/// </summary>
public interface ISessionAuthority : ITickVerifier
{
    /// <summary>
    /// Signs a simulation tick. The Ed25519 signature covers the canonical payload:
    /// Tick (big-endian int64) || PrevStateHash || StateHash || InputHash.
    /// </summary>
    /// <param name="tick">The simulation tick number.</param>
    /// <param name="prevStateHash">
    /// SHA-256 hash of the simulation state at the end of the previous signed checkpoint.
    /// Use the genesis state-hash sentinel (32 zero bytes) for the first tick.
    /// </param>
    /// <param name="stateHash">SHA-256 hash of the simulation state after this tick (32 bytes).</param>
    /// <param name="inputHash">SHA-256 hash of the player input(s) that drove this tick (32 bytes).</param>
    /// <returns>Ed25519 signature (64 bytes).</returns>
    byte[] SignTick(long tick, byte[] prevStateHash, byte[] stateHash, byte[] inputHash);
}
