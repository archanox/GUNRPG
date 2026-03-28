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
/// the <see cref="MerkleProof"/> (sibling hashes) are applied to
/// <see cref="ExpectedTickHash"/> (the tick leaf hash committed to the tree) to
/// reconstruct the Merkle root, and the result is compared against the root from the
/// <see cref="SignedRunResult.TickMerkleRoot"/>. A mismatch between
/// <see cref="ExpectedTickHash"/> and <see cref="ActualTickHash"/> demonstrates the
/// divergence.
/// </para>
/// <para>
/// Safety requirements enforced by <see cref="IsStructurallyValid"/>:
/// <see cref="ExpectedTickHash"/> must be exactly 32 bytes,
/// <see cref="ActualTickHash"/> must be exactly 32 bytes,
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
/// <param name="ActualTickHash">
/// The 32-byte state hash produced by the local simulation at <paramref name="TickIndex"/>.
/// When this differs from <paramref name="ExpectedTickHash"/> the divergence is confirmed.
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
    byte[] ActualTickHash,
    IReadOnlyList<byte[]> MerkleProof)
{
    private const int HashSize = SHA256.HashSizeInBytes;

    /// <summary>
    /// Returns <see langword="true"/> when all required fields are present with valid sizes:
    /// <list type="bullet">
    ///   <item><see cref="ExpectedTickHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="ActualTickHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="MerkleProof"/> is non-null.</item>
    ///   <item><see cref="LeafIndex"/> is non-negative.</item>
    /// </list>
    /// </summary>
    internal bool IsStructurallyValid =>
        ExpectedTickHash is not null && ExpectedTickHash.Length == HashSize &&
        ActualTickHash is not null && ActualTickHash.Length == HashSize &&
        MerkleProof is not null &&
        LeafIndex >= 0;
}
