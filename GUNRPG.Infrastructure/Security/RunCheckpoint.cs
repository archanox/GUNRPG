namespace GUNRPG.Security;

/// <summary>
/// Records the canonical state hash at a specific simulation tick,
/// enabling fast-path replay verification by skipping deterministic
/// segments when checkpoint hashes match.
/// </summary>
/// <remarks>
/// Checkpoints are embedded in a <see cref="SignedRunResult"/> and are
/// cryptographically bound to the authority signature via
/// <c>Hash(Checkpoints)</c> in the signing payload.  A verifier can therefore
/// trust a checkpoint state hash if and only if the enclosing
/// <see cref="SignedRunResult"/> signature is valid.
/// <para>
/// The caller is responsible for defensive copying of <paramref name="StateHash"/>;
/// the record does not clone the array.
/// </para>
/// </remarks>
/// <param name="TickIndex">
/// The simulation tick index at which this checkpoint was recorded.
/// Must be non-negative.
/// </param>
/// <param name="StateHash">
/// SHA-256 hash of the simulation state at <paramref name="TickIndex"/>.
/// Must be exactly 32 bytes.
/// </param>
public sealed record RunCheckpoint(long TickIndex, byte[] StateHash);
