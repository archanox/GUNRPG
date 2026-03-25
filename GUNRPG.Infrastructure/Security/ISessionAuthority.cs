using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Abstraction for an authority node that can sign and verify per-tick simulation state.
/// Implemented by <see cref="SessionAuthority"/> and can be mocked in tests.
/// </summary>
public interface ISessionAuthority
{
    /// <summary>Ed25519 public key of this authority node (32 bytes).</summary>
    byte[] PublicKey { get; }

    /// <summary>
    /// Signs a simulation tick. The Ed25519 signature covers the canonical payload:
    /// Tick (big-endian int64) ‖ StateHash ‖ InputHash.
    /// </summary>
    /// <param name="tick">The simulation tick number.</param>
    /// <param name="stateHash">SHA-256 hash of the simulation state after this tick (32 bytes).</param>
    /// <param name="inputHash">SHA-256 hash of the player input that drove this tick (32 bytes).</param>
    /// <returns>Ed25519 signature (64 bytes).</returns>
    byte[] SignTick(long tick, byte[] stateHash, byte[] inputHash);

    /// <summary>
    /// Verifies the Ed25519 signature on a <see cref="SignedTick"/> using this authority's public key.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the signature is valid; <see langword="false"/> otherwise.
    /// </returns>
    bool VerifyTick(SignedTick tick);
}
