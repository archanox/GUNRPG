using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GUNRPG.Security;

/// <summary>
/// A compact cryptographic certificate proving that a run exists and was signed
/// by the authority, without requiring the full replay data.
/// </summary>
/// <remarks>
/// A run receipt allows players to share or publish verifiable runs (for example
/// on leaderboards) using only a small payload. Any third party can validate the
/// receipt using only the receipt itself and the authority's public key — no
/// replay or tick data is required.
/// <para>
/// The <see cref="Signature"/> is an Ed25519 signature over the SHA-256 pre-hash
/// of the canonical payload:
/// <c>SessionId (16 bytes, big-endian) ||
/// FinalTick (big-endian int64) ||
/// len(FinalStateHash) (big-endian int32) || FinalStateHash ||
/// len(TickMerkleRoot) (big-endian int32) || TickMerkleRoot</c>.
/// </para>
/// <para>
/// Safety requirements enforced by <see cref="IsStructurallyValid"/>:
/// <see cref="FinalStateHash"/> must be exactly 32 bytes,
/// <see cref="TickMerkleRoot"/> must be exactly 32 bytes,
/// and <see cref="Signature"/> must be non-null and exactly 64 bytes.
/// </para>
/// </remarks>
/// <param name="SessionId">Run session identifier.</param>
/// <param name="FinalTick">Last tick index in the run.</param>
/// <param name="FinalStateHash">SHA-256 state hash at the end of the run (32 bytes).</param>
/// <param name="TickMerkleRoot">Merkle root of the signed tick chain (32 bytes).</param>
/// <param name="Signature">
/// Authority Ed25519 signature over the SHA-256 pre-hash of the canonical receipt payload.
/// </param>
public sealed record RunReceipt(
    Guid SessionId,
    long FinalTick,
    byte[] FinalStateHash,
    byte[] TickMerkleRoot,
    byte[] Signature)
{
    private const int HashSize = SHA256.HashSizeInBytes;
    private const int SignatureSize = AuthorityCrypto.SignatureSize;

    /// <summary>
    /// Returns <see langword="true"/> when all required fields are present with valid sizes:
    /// <list type="bullet">
    ///   <item><see cref="FinalStateHash"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="TickMerkleRoot"/> is non-null and exactly 32 bytes.</item>
    ///   <item><see cref="Signature"/> is non-null and exactly 64 bytes.</item>
    /// </list>
    /// </summary>
    public bool IsStructurallyValid =>
        FinalStateHash is not null && FinalStateHash.Length == HashSize &&
        TickMerkleRoot is not null && TickMerkleRoot.Length == HashSize &&
        Signature is not null && Signature.Length == SignatureSize;

    /// <summary>
    /// Serializes this receipt to a JSON string. Byte array fields are base64-encoded.
    /// If a byte array field is null (structurally invalid receipt), it is serialized
    /// as an empty string. Callers should ensure <see cref="IsStructurallyValid"/> is
    /// true before sharing a serialized receipt.
    /// </summary>
    public string ToJson()
    {
        var dto = new RunReceiptDto(
            SessionId,
            FinalTick,
            FinalStateHash is not null ? Convert.ToBase64String(FinalStateHash) : string.Empty,
            TickMerkleRoot is not null ? Convert.ToBase64String(TickMerkleRoot) : string.Empty,
            Signature is not null ? Convert.ToBase64String(Signature) : string.Empty);

        return JsonSerializer.Serialize(dto, RunReceiptJsonContext.Default.RunReceiptDto);
    }

    /// <summary>
    /// Deserializes a <see cref="RunReceipt"/> from a JSON string produced by <see cref="ToJson"/>.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized <see cref="RunReceipt"/>.</returns>
    /// <exception cref="JsonException">
    /// Thrown when <paramref name="json"/> is not valid JSON, a required field is missing or
    /// null, a hash field does not decode to exactly 32 bytes, or the signature field does not
    /// decode to exactly 64 bytes.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    public static RunReceipt FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var dto = JsonSerializer.Deserialize(json, RunReceiptJsonContext.Default.RunReceiptDto)
                  ?? throw new JsonException("Failed to deserialize RunReceipt: result was null.");

        byte[] finalStateHash;
        byte[] tickMerkleRoot;
        byte[] signature;

        try
        {
            if (dto.FinalStateHash is null)
                throw new JsonException("RunReceipt JSON is missing 'finalStateHash'.");
            finalStateHash = Convert.FromBase64String(dto.FinalStateHash);
            if (finalStateHash.Length != HashSize)
                throw new JsonException(
                    $"RunReceipt 'finalStateHash' must decode to {HashSize} bytes, got {finalStateHash.Length}.");

            if (dto.TickMerkleRoot is null)
                throw new JsonException("RunReceipt JSON is missing 'tickMerkleRoot'.");
            tickMerkleRoot = Convert.FromBase64String(dto.TickMerkleRoot);
            if (tickMerkleRoot.Length != HashSize)
                throw new JsonException(
                    $"RunReceipt 'tickMerkleRoot' must decode to {HashSize} bytes, got {tickMerkleRoot.Length}.");

            if (dto.Signature is null)
                throw new JsonException("RunReceipt JSON is missing 'signature'.");
            signature = Convert.FromBase64String(dto.Signature);
            if (signature.Length != SignatureSize)
                throw new JsonException(
                    $"RunReceipt 'signature' must decode to {SignatureSize} bytes, got {signature.Length}.");
        }
        catch (FormatException ex)
        {
            throw new JsonException("RunReceipt JSON contains an invalid base64-encoded field.", ex);
        }

        return new RunReceipt(dto.SessionId, dto.FinalTick, finalStateHash, tickMerkleRoot, signature);
    }
}

/// <summary>JSON DTO for <see cref="RunReceipt"/> serialization.</summary>
internal sealed record RunReceiptDto(
    Guid SessionId,
    long FinalTick,
    string FinalStateHash,
    string TickMerkleRoot,
    string Signature);

[JsonSerializable(typeof(RunReceiptDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class RunReceiptJsonContext : JsonSerializerContext;
