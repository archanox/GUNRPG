namespace GUNRPG.Security;

/// <summary>
/// Represents the signed result of a completed game session run.
/// Produced by <see cref="SessionAuthority.Sign"/> after session finalization
/// and verified via <see cref="SessionAuthority.VerifySignedRun"/>.
/// </summary>
public sealed class SignedRunResult
{
    private readonly byte[] _signature;

    /// <summary>Expected length of <see cref="FinalHash"/>: 64 hex chars for SHA-256.</summary>
    public const int FinalHashHexLength = 64;

    public SignedRunResult(
        Guid sessionId,
        Guid playerId,
        string finalHash,
        string authorityId,
        byte[] signature,
        string? replayHash = null,
        string? tickMerkleRoot = null)
    {
        if (string.IsNullOrWhiteSpace(finalHash))
            throw new ArgumentException("FinalHash must not be empty.", nameof(finalHash));
        if (finalHash.Length != FinalHashHexLength)
            throw new ArgumentException(
                $"FinalHash must be a {FinalHashHexLength}-character uppercase hex string (SHA-256).",
                nameof(finalHash));
        if (string.IsNullOrWhiteSpace(authorityId))
            throw new ArgumentException("AuthorityId must not be empty.", nameof(authorityId));
        if (replayHash is not null)
        {
            if (replayHash.Length != FinalHashHexLength)
                throw new ArgumentException(
                    $"ReplayHash must be a {FinalHashHexLength}-character uppercase hex string (SHA-256) when provided.",
                    nameof(replayHash));
        }

        if (tickMerkleRoot is not null)
        {
            if (replayHash is null)
                throw new ArgumentException(
                    "TickMerkleRoot can only be provided when ReplayHash is also provided.",
                    nameof(tickMerkleRoot));
            if (tickMerkleRoot.Length != FinalHashHexLength)
                throw new ArgumentException(
                    $"TickMerkleRoot must be a {FinalHashHexLength}-character uppercase hex string (SHA-256) when provided.",
                    nameof(tickMerkleRoot));
        }

        SessionId = sessionId;
        PlayerId = playerId;
        FinalHash = finalHash;
        AuthorityId = authorityId;
        ReplayHash = replayHash;
        TickMerkleRoot = tickMerkleRoot;
        _signature = AuthorityCrypto.CloneAndValidateSignature(signature);
    }

    /// <summary>Unique identifier of the session that was finalized.</summary>
    public Guid SessionId { get; }

    /// <summary>Identifier of the player (operator) who played the session.</summary>
    public Guid PlayerId { get; }

    /// <summary>SHA-256 hash of the replay-derived final session state, encoded as an uppercase hex string.</summary>
    public string FinalHash { get; }

    /// <summary>Identifier of the <see cref="Authority"/> whose private key produced this signature.</summary>
    public string AuthorityId { get; }

    /// <summary>
    /// SHA-256 hash of the full input log (replay hash), encoded as an uppercase hex string.
    /// Present only when the result was signed with the combined-payload overload
    /// (<see cref="SessionAuthority.Sign(Guid, Guid, byte[], byte[])"/>).
    /// When present the signature covers both <see cref="FinalHash"/> and this value.
    /// </summary>
    public string? ReplayHash { get; }

    /// <summary>Ed25519 signature over the validation payload hash.</summary>
    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;
}
