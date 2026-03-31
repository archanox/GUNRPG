using System.Security.Cryptography;

namespace GUNRPG.Security;

/// <summary>
/// A compact Merkle inclusion proof that cryptographically ties a specific event (tick leaf)
/// at <see cref="EventIndex"/> to a checkpoint root hash.
/// </summary>
/// <remarks>
/// <para>
/// The proof allows any peer to verify that a particular event contributed to a checkpoint root
/// without replaying the entire event log.  The sibling hashes form the path from the event
/// leaf up to the root.
/// </para>
/// <para>
/// Hashing scheme is identical to <see cref="MerkleTree"/>:
/// <list type="bullet">
///   <item>Leaf node: <c>SHA256(0x00 || leafData)</c></item>
///   <item>Internal node: <c>SHA256(0x01 || leftChild || rightChild)</c></item>
/// </list>
/// </para>
/// <para>
/// Use <see cref="MerkleTree.BuildCheckpointProof"/> to generate a proof and
/// <see cref="VerifyCheckpointProof"/> to verify one.
/// </para>
/// </remarks>
/// <param name="EventIndex">
/// Zero-based index of the event (tick leaf) being proved in the ordered event log.
/// Determines left/right ordering at each Merkle tree level during verification.
/// </param>
/// <param name="LeafHash">
/// The raw 32-byte SHA-256 leaf hash of the event.  The domain-separation prefix
/// (<c>0x00</c> byte) is applied internally by <see cref="VerifyCheckpointProof"/>
/// before reconstruction, mirroring the convention used by <see cref="MerkleTree"/>.
/// </param>
/// <param name="SiblingHashes">
/// Ordered list of sibling hashes from the event leaf level up to (but not including)
/// the root.  Each entry is the sibling subtree hash at that tree level.
/// The count must not exceed <see cref="MerkleTree.MaxMerkleProofDepth"/>.
/// </param>
public sealed record MerkleCheckpointProof(
    long EventIndex,
    byte[] LeafHash,
    IReadOnlyList<byte[]> SiblingHashes)
{
    private const int HashSize = SHA256.HashSizeInBytes;

    /// <summary>
    /// Returns <see langword="true"/> when all required fields are structurally valid:
    /// <list type="bullet">
    ///   <item><see cref="EventIndex"/> is non-negative.</item>
    ///   <item><see cref="LeafHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="SiblingHashes"/> is non-null, within depth limit, and every entry is non-null and exactly 32 bytes.</item>
    /// </list>
    /// Does not verify the proof cryptographically.
    /// </summary>
    public bool IsStructurallyValid
    {
        get
        {
            if (EventIndex < 0)
                return false;
            if (LeafHash is null || LeafHash.Length != HashSize)
                return false;
            if (SiblingHashes is null || SiblingHashes.Count > MerkleTree.MaxMerkleProofDepth)
                return false;
            foreach (var s in SiblingHashes)
            {
                if (s is null || s.Length != HashSize)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Verifies that the event described by <paramref name="proof"/> is part of the Merkle tree
    /// whose root is <paramref name="expectedRoot"/>.
    /// </summary>
    /// <remarks>
    /// Recomputes the root by applying the leaf domain-separation prefix (<c>SHA256(0x00 || leafHash)</c>)
    /// and then successively combining with each sibling hash in <see cref="SiblingHashes"/>
    /// using the left/right ordering determined by <see cref="EventIndex"/>.
    /// The proof is valid when the recomputed root equals <paramref name="expectedRoot"/>.
    /// </remarks>
    /// <param name="proof">The proof to verify.  Must not be <see langword="null"/>.</param>
    /// <param name="expectedRoot">
    /// The expected 32-byte Merkle root.  Must not be <see langword="null"/>
    /// and must be exactly 32 bytes.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="proof"/> is structurally valid and the
    /// recomputed root matches <paramref name="expectedRoot"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="proof"/> or <paramref name="expectedRoot"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="expectedRoot"/> is not exactly 32 bytes.
    /// </exception>
    public static bool VerifyCheckpointProof(MerkleCheckpointProof proof, byte[] expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(expectedRoot);
        if (expectedRoot.Length != HashSize)
            throw new ArgumentException(
                $"expectedRoot must be exactly {HashSize} bytes.", nameof(expectedRoot));

        if (!proof.IsStructurallyValid)
            return false;

        // Apply leaf domain separator: SHA256(0x00 || leafData)
        var current = ComputeLeafNode(proof.LeafHash);
        var index = proof.EventIndex;

        foreach (var sibling in proof.SiblingHashes)
        {
            current = index % 2 == 0
                ? ComputeParentHash(current, sibling)
                : ComputeParentHash(sibling, current);
            index /= 2;
        }

        return current.AsSpan().SequenceEqual(expectedRoot);
    }

    /// <summary>Applies the leaf domain prefix: <c>SHA256(0x00 || leafData)</c>.</summary>
    private static byte[] ComputeLeafNode(byte[] leafData)
    {
        var buffer = new byte[1 + HashSize];
        buffer[0] = 0x00;
        leafData.CopyTo(buffer, 1);
        return SHA256.HashData(buffer);
    }

    /// <summary>Combines two child nodes: <c>SHA256(0x01 || left || right)</c>.</summary>
    private static byte[] ComputeParentHash(byte[] left, byte[] right)
    {
        var buffer = new byte[1 + HashSize + HashSize];
        buffer[0] = 0x01;
        left.CopyTo(buffer, 1);
        right.CopyTo(buffer, 1 + HashSize);
        return SHA256.HashData(buffer);
    }
}
