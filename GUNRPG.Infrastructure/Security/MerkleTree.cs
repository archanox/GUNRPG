using System.Security.Cryptography;

namespace GUNRPG.Security;

/// <summary>
/// Utility for building a binary Merkle tree over tick leaf hashes and generating or
/// verifying Merkle inclusion proofs.
/// </summary>
/// <remarks>
/// Tree rules:
/// <list type="bullet">
///   <item>If the number of leaves at any level is odd the last leaf is duplicated.</item>
///   <item>
///     Leaf node: <c>SHA256(0x00 || leafData)</c> — the <c>0x00</c> domain-separation byte
///     prevents leaf/node hash collisions.
///   </item>
///   <item>
///     Internal node: <c>SHA256(0x01 || leftChild || rightChild)</c> — the <c>0x01</c> byte
///     ensures internal nodes can never be confused with leaf nodes.
///   </item>
///   <item>An empty leaf list returns a 32-byte zero root.</item>
/// </list>
/// </remarks>
public sealed class MerkleTree
{
    private const int HashSize = SHA256.HashSizeInBytes;

    /// <summary>
    /// Maximum allowed depth (number of sibling hashes) in an inclusion proof.
    /// A tree with 2^64 leaves would have depth 64; any proof deeper than this
    /// is pathological and rejected to prevent denial-of-service via excessively
    /// large proof inputs.
    /// </summary>
    public const int MaxMerkleProofDepth = 64;

    /// <summary>
    /// Computes the Merkle root of the given leaf hashes.
    /// </summary>
    /// <param name="leaves">
    /// The ordered list of leaf hashes (each must be exactly 32 bytes).
    /// </param>
    /// <returns>The 32-byte Merkle root.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaves"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when any leaf is <see langword="null"/> or not exactly 32 bytes.</exception>
    public static byte[] ComputeRoot(IReadOnlyList<byte[]> leaves)
    {
        ArgumentNullException.ThrowIfNull(leaves);

        if (leaves.Count == 0)
            return new byte[HashSize];

        var layer = BuildInitialLayer(leaves);

        while (layer.Count > 1)
            layer = BuildNextLayer(layer);

        return layer[0];
    }

    /// <summary>
    /// Generates a Merkle inclusion proof for the leaf at <paramref name="leafIndex"/>.
    /// </summary>
    /// <param name="leaves">The ordered list of leaf hashes.</param>
    /// <param name="leafIndex">Zero-based index of the leaf to prove.</param>
    /// <returns>A <see cref="MerkleProof"/> that can be verified against the Merkle root.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaves"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="leafIndex"/> is out of range.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when any leaf is <see langword="null"/> or not exactly 32 bytes.</exception>
    public static MerkleProof GenerateProof(IReadOnlyList<byte[]> leaves, int leafIndex)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        if (leafIndex < 0 || leafIndex >= leaves.Count)
            throw new ArgumentOutOfRangeException(nameof(leafIndex),
                $"leafIndex {leafIndex} is out of range for a leaf list of length {leaves.Count}.");

        var layer = BuildInitialLayer(leaves);
        var siblings = new List<byte[]>();
        var index = leafIndex;

        while (layer.Count > 1)
        {
            var siblingIndex = (index % 2 == 0) ? index + 1 : index - 1;
            // If sibling is beyond the end (odd layer) the last leaf was duplicated.
            siblings.Add(siblingIndex < layer.Count
                ? (byte[])layer[siblingIndex].Clone()
                : (byte[])layer[index].Clone());

            layer = BuildNextLayer(layer);
            index /= 2;
        }

        return new MerkleProof((byte[])leaves[leafIndex].Clone(), siblings, leafIndex);
    }

    /// <summary>
    /// Verifies that a Merkle proof is consistent with the provided <paramref name="root"/>.
    /// </summary>
    /// <param name="proof">The proof to verify.</param>
    /// <param name="root">The expected Merkle root (32 bytes).</param>
    /// <returns>
    /// <see langword="true"/> if the proof is valid and the recomputed root matches
    /// <paramref name="root"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="proof"/> or <paramref name="root"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="root"/> is not exactly 32 bytes,
    /// when <see cref="MerkleProof.LeafIndex"/> is negative,
    /// when <see cref="MerkleProof.LeafHash"/> is <see langword="null"/> or not exactly 32 bytes,
    /// when <see cref="MerkleProof.SiblingHashes"/> is <see langword="null"/> or exceeds
    /// <see cref="MaxMerkleProofDepth"/> entries (DoS protection),
    /// or when any sibling hash is <see langword="null"/> or not exactly 32 bytes.
    /// </exception>
    public static bool VerifyProof(MerkleProof proof, byte[] root)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(root);
        if (root.Length != HashSize)
            throw new ArgumentException($"Root must be exactly {HashSize} bytes.", nameof(root));

        if (proof.LeafIndex < 0)
            throw new ArgumentException("proof.LeafIndex must be non-negative.", nameof(proof));

        if (proof.LeafHash is null || proof.LeafHash.Length != HashSize)
            throw new ArgumentException(
                $"proof.LeafHash must be a non-null {HashSize}-byte SHA-256 hash.", nameof(proof));

        if (proof.SiblingHashes is null)
            throw new ArgumentException("proof.SiblingHashes must not be null.", nameof(proof));

        if (proof.SiblingHashes.Count > MaxMerkleProofDepth)
            throw new ArgumentException(
                $"Merkle proof depth ({proof.SiblingHashes.Count}) exceeds the allowed limit of {MaxMerkleProofDepth}.",
                nameof(proof));

        // Apply leaf-domain prefix (0x00) before traversal so leaf nodes cannot
        // be confused with internal nodes (0x01 prefix).
        var current = ComputeLeafNode(proof.LeafHash);
        var index = proof.LeafIndex;

        var siblingIndex = 0;
        foreach (var sibling in proof.SiblingHashes)
        {
            if (sibling is null || sibling.Length != HashSize)
                throw new ArgumentException(
                    $"Sibling hash at index {siblingIndex} must be a non-null {HashSize}-byte SHA-256 hash.",
                    nameof(proof));

            current = index % 2 == 0
                ? ComputeParentHash(current, sibling)
                : ComputeParentHash(sibling, current);
            index /= 2;
            siblingIndex++;
        }

        return current.AsSpan().SequenceEqual(root);
    }

    private static List<byte[]> BuildInitialLayer(IReadOnlyList<byte[]> leaves)
    {
        var layer = new List<byte[]>(leaves.Count);
        for (var i = 0; i < leaves.Count; i++)
        {
            var leaf = leaves[i];
            if (leaf is null || leaf.Length != HashSize)
                throw new ArgumentException(
                    $"Leaf at index {i} must be a non-null {HashSize}-byte SHA-256 hash.",
                    "leaves");
            layer.Add(ComputeLeafNode(leaf));
        }

        return layer;
    }

    private static List<byte[]> BuildNextLayer(List<byte[]> layer)
    {
        var next = new List<byte[]>((layer.Count + 1) / 2);
        for (var i = 0; i < layer.Count; i += 2)
        {
            var left = layer[i];
            var right = i + 1 < layer.Count ? layer[i + 1] : layer[i]; // duplicate last if odd
            next.Add(ComputeParentHash(left, right));
        }

        return next;
    }

    /// <summary>
    /// Applies the leaf-domain prefix: <c>SHA256(0x00 || leafData)</c>.
    /// </summary>
    private static byte[] ComputeLeafNode(byte[] leafData)
    {
        var buffer = new byte[1 + HashSize];
        buffer[0] = 0x00;
        leafData.CopyTo(buffer, 1);
        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Combines two child nodes: <c>SHA256(0x01 || left || right)</c>.
    /// The <c>0x01</c> byte ensures internal nodes are distinct from leaf nodes.
    /// </summary>
    private static byte[] ComputeParentHash(byte[] left, byte[] right)
    {
        var buffer = new byte[1 + HashSize + HashSize];
        buffer[0] = 0x01;
        left.CopyTo(buffer, 1);
        right.CopyTo(buffer, 1 + HashSize);
        return SHA256.HashData(buffer);
    }
}
