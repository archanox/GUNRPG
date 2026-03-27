using System.Security.Cryptography;

namespace GUNRPG.Security;

/// <summary>
/// An incremental Merkle tree accumulator (Merkle frontier) that computes the Merkle root
/// in O(log n) memory without storing all leaf hashes.
/// </summary>
/// <remarks>
/// <para>
/// Each slot in the frontier represents a completed subtree of size 2^k.
/// Adding a leaf performs a binary-carry merge: if a slot at level k is occupied,
/// the two subtrees are merged into a parent at level k+1, mirroring binary addition.
/// </para>
/// <para>
/// The hashing scheme is identical to <see cref="MerkleTree"/>:
/// <list type="bullet">
///   <item>Leaf node: <c>SHA256(0x00 || leafData)</c></item>
///   <item>Internal node: <c>SHA256(0x01 || leftChild || rightChild)</c></item>
/// </list>
/// </para>
/// <para>
/// <see cref="ComputeRoot"/> produces a result identical to
/// <see cref="MerkleTree.ComputeRoot"/> for the same ordered set of leaves,
/// including correct handling of non-power-of-two leaf counts via same-node duplication.
/// </para>
/// </remarks>
public sealed class MerkleFrontier
{
    private const int HashSize = SHA256.HashSizeInBytes;

    // Each slot is either null (empty) or a completed subtree hash at level k (size 2^k).
    private readonly List<byte[]?> _levels = [];

    /// <summary>Gets the total number of leaves added so far.</summary>
    public long LeafCount { get; private set; }

    /// <summary>
    /// Adds a leaf hash to the frontier, merging carry subtrees as needed.
    /// </summary>
    /// <param name="leafHash">The 32-byte SHA-256 leaf hash (pre-image of the leaf data).</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leafHash"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leafHash"/> is not exactly 32 bytes.</exception>
    public void AddLeaf(byte[] leafHash)
    {
        ArgumentNullException.ThrowIfNull(leafHash);
        if (leafHash.Length != HashSize)
            throw new ArgumentException(
                $"leafHash must be exactly {HashSize} bytes.",
                nameof(leafHash));

        // Apply leaf domain separator: SHA256(0x00 || leafHash)
        var node = ComputeLeafNode(leafHash);
        LeafCount++;

        // Carry-merge upward until an empty slot is found.
        var level = 0;
        while (true)
        {
            if (level == _levels.Count)
            {
                // Grow the frontier: no existing slot at this level.
                _levels.Add(node);
                break;
            }

            if (_levels[level] is null)
            {
                // Empty slot: store the node here.
                _levels[level] = node;
                break;
            }

            // Occupied slot: merge left (existing) with right (new), then carry up.
            node = ComputeParentHash(_levels[level]!, node);
            _levels[level] = null;
            level++;
        }
    }

    /// <summary>
    /// Computes the Merkle root of all leaves added so far.
    /// </summary>
    /// <remarks>
    /// Partial subtrees are combined bottom-up. When a partial subtree is smaller than
    /// the subtree it is being paired with, it is padded by hashing with itself until the
    /// sizes match — exactly replicating the duplicate-last-node behaviour of
    /// <see cref="MerkleTree.ComputeRoot"/>.
    /// </remarks>
    /// <returns>
    /// The 32-byte Merkle root, or a 32-byte zero array when no leaves have been added.
    /// </returns>
    public byte[] ComputeRoot()
    {
        if (LeafCount == 0)
            return new byte[HashSize];

        byte[]? root = null;
        var rootLevel = -1;

        for (var k = 0; k < _levels.Count; k++)
        {
            if (_levels[k] is null)
                continue;

            var node = _levels[k]!;

            if (root is null)
            {
                root = node;
                rootLevel = k;
            }
            else
            {
                // Pad the accumulated root upward until it matches level k.
                // This replicates the "duplicate last node" behaviour for odd subtrees.
                while (rootLevel < k)
                {
                    root = ComputeParentHash(root, root);
                    rootLevel++;
                }

                // Combine: frontier[k] is the left subtree (added earlier),
                // root is the right partial subtree.
                root = ComputeParentHash(node, root);
                rootLevel = k + 1;
            }
        }

        return root!;
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
