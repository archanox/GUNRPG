using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GUNRPG.Security;

/// <summary>
/// A trusted snapshot of the Merkle state at a specific tick, signed by an authority node.
/// Allows verifiers to resume verification from a known-good state rather than replaying
/// from tick 0, enabling faster distributed verification of long runs.
/// </summary>
/// <remarks>
/// <para>
/// The signing payload is <c>SHA-256("checkpoint" || Tick (big-endian uint64) || MerkleRoot)</c>.
/// The signature is an Ed25519 signature over that hash.
/// </para>
/// <para>
/// Checkpoints must be strictly ordered: every subsequent checkpoint must have a higher
/// <see cref="Tick"/> than the previous one.  Duplicate or rewound checkpoints are rejected
/// by <see cref="CheckOrdering"/>.
/// </para>
/// <para>
/// Use <see cref="SessionAuthority.CreateMerkleCheckpoint"/> to produce a checkpoint and
/// <see cref="SessionAuthority.VerifyMerkleCheckpoint"/> to validate one.
/// </para>
/// </remarks>
/// <param name="Tick">
/// The tick index this checkpoint represents.
/// </param>
/// <param name="MerkleRoot">
/// The simulation state hash at <see cref="Tick"/> (SHA-256, 32 bytes).
/// Acts as the cryptographic anchor for resuming replay verification from this tick.
/// </param>
/// <param name="AuthorityPublicKey">
/// The Ed25519 public key (32 bytes) of the authority that signed this checkpoint.
/// Must be present in the <see cref="AuthorityRegistry"/> to be considered trusted.
/// </param>
/// <param name="Signature">
/// Ed25519 signature (64 bytes) over
/// <c>SHA-256("checkpoint" || Tick (big-endian uint64) || MerkleRoot)</c>.
/// </param>
public sealed record MerkleCheckpoint(
    ulong Tick,
    byte[] MerkleRoot,
    byte[] AuthorityPublicKey,
    byte[] Signature)
{
    /// <summary>
    /// Returns <see langword="true"/> if the field lengths are structurally valid
    /// (32-byte <see cref="MerkleRoot"/>, 32-byte <see cref="AuthorityPublicKey"/>,
    /// 64-byte <see cref="Signature"/>).
    /// Does not verify the cryptographic signature.
    /// </summary>
    public bool HasValidStructure =>
        MerkleRoot is not null && MerkleRoot.Length == SHA256.HashSizeInBytes &&
        AuthorityPublicKey is not null && AuthorityPublicKey.Length == AuthorityCrypto.KeySize &&
        Signature is not null && Signature.Length == AuthorityCrypto.SignatureSize;

    /// <summary>
    /// Verifies that <paramref name="next"/> has a strictly higher <see cref="Tick"/> than
    /// <paramref name="previous"/>, preventing duplicate or rewound checkpoints.
    /// </summary>
    /// <param name="previous">The most recently accepted checkpoint.</param>
    /// <param name="next">The candidate next checkpoint to accept.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="next"/> is strictly after <paramref name="previous"/>;
    /// <see langword="false"/> if <paramref name="next"/> is a duplicate or has a lower tick.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when either parameter is <see langword="null"/>.
    /// </exception>
    public static bool CheckOrdering(MerkleCheckpoint previous, MerkleCheckpoint next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        return next.Tick > previous.Tick;
    }

    /// <summary>
    /// Serialises the checkpoint to a JSON byte array suitable for storage.
    /// Fields are base-64 encoded as per the standard JSON mapping for <c>byte[]</c>.
    /// </summary>
    public byte[] ToJsonBytes()
    {
        var dto = new MerkleCheckpointDto(
            Tick,
            Convert.ToBase64String(MerkleRoot),
            Convert.ToBase64String(AuthorityPublicKey),
            Convert.ToBase64String(Signature));
        return JsonSerializer.SerializeToUtf8Bytes(dto, MerkleCheckpointJsonContext.Default.MerkleCheckpointDto);
    }

    /// <summary>
    /// Deserialises a checkpoint from a JSON byte array produced by <see cref="ToJsonBytes"/>.
    /// </summary>
    /// <exception cref="JsonException">
    /// Thrown when the JSON is malformed or a required field is missing or invalid.
    /// </exception>
    public static MerkleCheckpoint FromJsonBytes(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var dto = JsonSerializer.Deserialize(json, MerkleCheckpointJsonContext.Default.MerkleCheckpointDto)
                  ?? throw new JsonException("Failed to deserialise MerkleCheckpoint: result was null.");

        byte[] merkleRoot, authorityPublicKey, signature;
        try
        {
            merkleRoot = Convert.FromBase64String(dto.MerkleRoot
                ?? throw new JsonException("MerkleCheckpoint.MerkleRoot is required."));
            authorityPublicKey = Convert.FromBase64String(dto.AuthorityPublicKey
                ?? throw new JsonException("MerkleCheckpoint.AuthorityPublicKey is required."));
            signature = Convert.FromBase64String(dto.Signature
                ?? throw new JsonException("MerkleCheckpoint.Signature is required."));
        }
        catch (FormatException ex)
        {
            throw new JsonException("MerkleCheckpoint field is not valid base-64.", ex);
        }

        if (merkleRoot.Length != SHA256.HashSizeInBytes)
            throw new JsonException(
                $"MerkleCheckpoint.MerkleRoot must be {SHA256.HashSizeInBytes} bytes after base-64 decoding.");
        if (authorityPublicKey.Length != AuthorityCrypto.KeySize)
            throw new JsonException(
                $"MerkleCheckpoint.AuthorityPublicKey must be {AuthorityCrypto.KeySize} bytes after base-64 decoding.");
        if (signature.Length != AuthorityCrypto.SignatureSize)
            throw new JsonException(
                $"MerkleCheckpoint.Signature must be {AuthorityCrypto.SignatureSize} bytes after base-64 decoding.");

        return new MerkleCheckpoint(dto.Tick, merkleRoot, authorityPublicKey, signature);
    }
}

/// <summary>JSON DTO for <see cref="MerkleCheckpoint"/> serialisation.</summary>
internal sealed record MerkleCheckpointDto(
    ulong Tick,
    string? MerkleRoot,
    string? AuthorityPublicKey,
    string? Signature);

[JsonSerializable(typeof(MerkleCheckpointDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class MerkleCheckpointJsonContext : JsonSerializerContext;
