namespace GUNRPG.Security;

/// <summary>
/// A Merkle inclusion proof that allows verifying that a specific tick's leaf hash
/// is part of a Merkle tree without replaying the entire tick chain.
/// </summary>
/// <param name="LeafHash">The SHA-256 leaf hash of the tick being proved.</param>
/// <param name="SiblingHashes">
/// The ordered list of sibling hashes from the leaf up to (but not including) the root.
/// Each entry is the sibling at that tree level.
/// </param>
/// <param name="LeafIndex">The zero-based index of the leaf in the original leaf list.</param>
public sealed record MerkleProof(
    byte[] LeafHash,
    IReadOnlyList<byte[]> SiblingHashes,
    int LeafIndex);
