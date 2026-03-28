using System.Security.Cryptography;

namespace GUNRPG.Security;

/// <summary>
/// A signed state snapshot captured at a checkpoint tick boundary.
/// Allows verifiers to start replay from an intermediate trusted state instead
/// of always replaying from tick 0, dramatically reducing verification cost for
/// long runs.
/// </summary>
/// <remarks>
/// A snapshot is cryptographically bound to a session via the Ed25519
/// <see cref="Signature"/> over <c>SessionId || TickIndex || StateHash || SerializedState</c>.
/// Verifiers must confirm the signature before trusting the snapshot payload.
/// <para>
/// The <see cref="TickIndex"/> must match a checkpoint tick in the enclosing
/// <see cref="SignedRunResult.Checkpoints"/> list, and <see cref="StateHash"/> must equal
/// the checkpoint's <see cref="RunCheckpoint.StateHash"/>.
/// </para>
/// </remarks>
/// <param name="TickIndex">
/// Simulation tick index at which this snapshot was captured.
/// Must correspond to a checkpoint tick in the run's <see cref="SignedRunResult.Checkpoints"/>.
/// </param>
/// <param name="StateHash">
/// SHA-256 hash of the simulation state at <paramref name="TickIndex"/>.
/// Must be exactly 32 bytes and must match the corresponding
/// <see cref="RunCheckpoint.StateHash"/> in the enclosing <see cref="SignedRunResult"/>.
/// </param>
/// <param name="SerializedState">
/// Deterministic serialization of the simulation state at <paramref name="TickIndex"/>,
/// produced by <see cref="IDeterministicSimulation.SerializeState"/>.
/// </param>
/// <param name="Signature">
/// Ed25519 signature from the session authority over the snapshot payload:
/// <c>SessionId || TickIndex (big-endian int64) || StateHash (length-prefixed) || SerializedState (length-prefixed)</c>.
/// </param>
public sealed record StateSnapshot(
    long TickIndex,
    byte[] StateHash,
    byte[] SerializedState,
    byte[] Signature)
{
    /// <summary>
    /// Returns <see langword="true"/> when <see cref="StateHash"/> is a valid 32-byte SHA-256 hash.
    /// </summary>
    internal bool HasValidHashLength =>
        StateHash is not null && StateHash.Length == SHA256.HashSizeInBytes;
}
