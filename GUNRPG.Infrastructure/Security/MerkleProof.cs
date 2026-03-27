namespace GUNRPG.Security;

/// <summary>
/// A Merkle inclusion proof that allows verifying that a specific tick's leaf hash
/// is part of a Merkle tree without replaying the entire tick chain.
/// </summary>
/// <remarks>
/// Declared as a regular class (not a record) to avoid the record's default reference equality
/// on <see cref="LeafHash"/> / <see cref="SiblingHashes"/>, which could cause subtle bugs
/// when proofs are compared or used as dictionary keys.
/// </remarks>
public sealed class MerkleProof
{
    /// <summary>Initializes a new <see cref="MerkleProof"/>.</summary>
    /// <param name="leafHash">The SHA-256 leaf hash of the tick being proved.</param>
    /// <param name="siblingHashes">
    /// The ordered list of sibling hashes from the leaf up to (but not including) the root.
    /// Each entry is the sibling at that tree level.
    /// </param>
    /// <param name="leafIndex">The zero-based index of the leaf in the original leaf list.</param>
    public MerkleProof(byte[] leafHash, IReadOnlyList<byte[]> siblingHashes, int leafIndex)
    {
        LeafHash = leafHash;
        SiblingHashes = siblingHashes;
        LeafIndex = leafIndex;
    }

    /// <summary>The SHA-256 leaf hash of the tick being proved.</summary>
    public byte[] LeafHash { get; }

    /// <summary>
    /// The ordered list of sibling hashes from the leaf up to (but not including) the root.
    /// Each entry is the sibling at that tree level.
    /// </summary>
    public IReadOnlyList<byte[]> SiblingHashes { get; }

    /// <summary>The zero-based index of the leaf in the original leaf list.</summary>
    public int LeafIndex { get; }
}
