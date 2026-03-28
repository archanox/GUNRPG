using System.Security.Cryptography;

namespace GUNRPG.Security;

/// <summary>
/// A compact cryptographic proof that a specific tick in a run diverges from the
/// authority-signed Merkle tree.
/// </summary>
/// <remarks>
/// A divergence proof allows any third party to verify that a run is invalid at a
/// specific tick without replaying the full simulation.
/// <para>
/// Verification is performed by <see cref="ReplayVerifier.VerifyDivergenceProof"/>:
/// <list type="number">
///   <item>
///     The <see cref="MerkleProof"/> (sibling hashes) are applied to
///     <see cref="ExpectedTickHash"/> (the tick leaf hash committed to the Merkle tree)
///     to reconstruct the Merkle root and confirm it matches
///     <see cref="SignedRunResult.TickMerkleRoot"/>, proving the authority signed this tick.
///   </item>
///   <item>
///     <see cref="ExpectedStateHash"/> (the state hash the authority committed to this tick,
///     i.e., <see cref="SignedTick.StateHash"/>) is compared against
///     <see cref="ActualStateHash"/> (the state hash the local simulation produced).
///     A difference between these two values — both in the same state-hash domain —
///     demonstrates the divergence.
///   </item>
/// </list>
/// </para>
/// <para>
/// Safety requirements enforced by <see cref="IsStructurallyValid"/>:
/// <see cref="ExpectedTickHash"/> must be exactly 32 bytes,
/// <see cref="ExpectedStateHash"/> must be exactly 32 bytes,
/// <see cref="ActualStateHash"/> must be exactly 32 bytes,
/// <see cref="MerkleProof"/> must be non-null,
/// and <see cref="LeafIndex"/> must be non-negative.
/// </para>
/// </remarks>
/// <param name="TickIndex">
/// The simulation tick number (<see cref="SignedTick.Tick"/>) at which divergence was detected.
/// </param>
/// <param name="LeafIndex">
/// The zero-based index of this tick in the ordered tick chain (and therefore in the Merkle
/// tree's leaf array). Required so that sibling hashes in <see cref="MerkleProof"/> can be
/// assigned to the correct side (left or right) at each tree level during verification.
/// </param>
/// <param name="ExpectedTickHash">
/// The 32-byte tick leaf hash committed to the run's Merkle tree, computed via
/// <see cref="TickAuthorityService.ComputeTickLeafHash"/>.
/// The Merkle inclusion proof in <see cref="MerkleProof"/> is relative to this hash.
/// </param>
/// <param name="ExpectedStateHash">
/// The 32-byte state hash that the authority committed to at <paramref name="TickIndex"/>
/// (i.e., <see cref="SignedTick.StateHash"/>). This is the value the authority attested
/// the simulation should produce.
/// </param>
/// <param name="ActualStateHash">
/// The 32-byte state hash produced by the local simulation at <paramref name="TickIndex"/>.
/// Divergence is confirmed when this differs from <paramref name="ExpectedStateHash"/>.
/// </param>
/// <param name="MerkleProof">
/// The ordered list of sibling hashes needed to recompute the Merkle root from
/// <paramref name="ExpectedTickHash"/>. Produced by
/// <see cref="MerkleTree.GenerateProof"/> and verified by
/// <see cref="MerkleTree.VerifyProof"/>.
/// </param>
public sealed record DivergenceProof(
    long TickIndex,
    int LeafIndex,
    byte[] ExpectedTickHash,
    byte[] ExpectedStateHash,
    byte[] ActualStateHash,
    IReadOnlyList<byte[]> MerkleProof)
{
    private const int HashSize = SHA256.HashSizeInBytes;

    /// <summary>
    /// Returns <see langword="true"/> when all required fields are present with valid sizes:
    /// <list type="bullet">
    ///   <item><see cref="ExpectedTickHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="ExpectedStateHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="ActualStateHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="MerkleProof"/> is non-null.</item>
    ///   <item><see cref="LeafIndex"/> is non-negative.</item>
    /// </list>
    /// </summary>
    internal bool IsStructurallyValid =>
        ExpectedTickHash is not null && ExpectedTickHash.Length == HashSize &&
        ExpectedStateHash is not null && ExpectedStateHash.Length == HashSize &&
        ActualStateHash is not null && ActualStateHash.Length == HashSize &&
        MerkleProof is not null &&
        LeafIndex >= 0;
}
