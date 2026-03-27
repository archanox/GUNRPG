namespace GUNRPG.Security;

/// <summary>
/// A streaming accumulator for building a Merkle tree root incrementally.
/// Leaf hashes are added one at a time via <see cref="AddLeaf"/>; the root is
/// computed on demand by calling <see cref="BuildRoot"/>.
/// </summary>
/// <remarks>
/// Internally delegates to <see cref="MerkleFrontier"/>, which maintains O(log n) state
/// and avoids materialising the full leaf list.
/// </remarks>
public sealed class MerkleBuilder
{
    private readonly MerkleFrontier _frontier = new();

    /// <summary>Gets the number of leaf hashes added so far.</summary>
    public int Count => (int)_frontier.LeafCount;

    /// <summary>
    /// Adds a leaf hash to the builder.
    /// </summary>
    /// <param name="leafHash">The 32-byte SHA-256 leaf hash.</param>
    /// <returns>This builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leafHash"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="leafHash"/> is not exactly 32 bytes.</exception>
    public MerkleBuilder AddLeaf(byte[] leafHash)
    {
        _frontier.AddLeaf(leafHash);
        return this;
    }

    /// <summary>
    /// Computes and returns the Merkle root of all leaves added so far.
    /// Returns a 32-byte zero array when no leaves have been added.
    /// </summary>
    /// <returns>The 32-byte Merkle root.</returns>
    public byte[] BuildRoot() => _frontier.ComputeRoot();
}
