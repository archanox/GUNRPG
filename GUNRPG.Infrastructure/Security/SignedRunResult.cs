namespace GUNRPG.Security;

/// <summary>
/// Represents the signed result of a completed game session run.
/// Produced by <see cref="SessionAuthority.Sign"/> after session finalization
/// and verified via <see cref="SessionAuthority.VerifySignedRun"/>.
/// </summary>
public sealed class SignedRunResult
{
    private readonly byte[] _signature;

    public SignedRunResult(
        Guid sessionId,
        Guid playerId,
        string finalHash,
        string authorityId,
        byte[] signature)
    {
        if (string.IsNullOrWhiteSpace(finalHash))
            throw new ArgumentException("FinalHash must not be empty.", nameof(finalHash));
        if (string.IsNullOrWhiteSpace(authorityId))
            throw new ArgumentException("AuthorityId must not be empty.", nameof(authorityId));

        SessionId = sessionId;
        PlayerId = playerId;
        FinalHash = finalHash;
        AuthorityId = authorityId;
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

    /// <summary>Ed25519 signature over the validation payload hash.</summary>
    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;
}
