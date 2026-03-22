using GUNRPG.Application.Sessions;

namespace GUNRPG.Security;

/// <summary>
/// An authority node that can sign completed session runs and verify signed results.
/// Bridges the session replay system with the cryptographic authority infrastructure.
/// </summary>
public sealed class SessionAuthority
{
    private readonly byte[] _privateKey;
    private readonly byte[] _publicKey;

    public SessionAuthority(byte[] privateKey, string id)
    {
        _privateKey = AuthorityCrypto.CloneAndValidatePrivateKey(privateKey);
        _publicKey = AuthorityCrypto.GetPublicKey(_privateKey);
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Authority id must not be empty.", nameof(id))
            : id;
    }

    /// <summary>Unique identifier for this authority node.</summary>
    public string Id { get; }

    /// <summary>Public key bytes for this authority (Ed25519, 32 bytes).</summary>
    public byte[] PublicKey => (byte[])_publicKey.Clone();

    /// <summary>Returns a read-only <see cref="Authority"/> view of this node's public identity.</summary>
    public Authority ToAuthority() => new(_publicKey, Id);

    /// <summary>
    /// Signs a finalized session run and returns a <see cref="SignedRunResult"/>.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="playerId">The player/operator unique identifier.</param>
    /// <param name="finalHashBytes">
    /// The SHA-256 hash produced by <see cref="CombatSessionHasher.ComputeStateHash"/> after replay.
    /// Must be exactly 32 bytes.
    /// </param>
    public SignedRunResult Sign(Guid sessionId, Guid playerId, byte[] finalHashBytes)
    {
        ArgumentNullException.ThrowIfNull(finalHashBytes);
        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"finalHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(finalHashBytes));

        var payloadHash = AuthorityCrypto.ComputeRunValidationPayloadHash(sessionId, playerId, finalHashBytes);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        var finalHashHex = Convert.ToHexString(finalHashBytes);
        return new SignedRunResult(sessionId, playerId, finalHashHex, Id, signature);
    }

    /// <summary>
    /// Verifies that a <see cref="SignedRunResult"/> was produced by the given <paramref name="authority"/>.
    /// Returns <see langword="false"/> if the authority ID does not match, the signature is invalid,
    /// or <see cref="SignedRunResult.FinalHash"/> cannot be decoded as a valid 32-byte hex value.
    /// </summary>
    public static bool VerifySignedRun(SignedRunResult result, Authority authority)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(authority);

        if (!string.Equals(result.AuthorityId, authority.Id, StringComparison.Ordinal))
            return false;

        byte[] finalHashBytes;
        try
        {
            finalHashBytes = Convert.FromHexString(result.FinalHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            return false;

        var payloadHash = AuthorityCrypto.ComputeRunValidationPayloadHash(
            result.SessionId,
            result.PlayerId,
            finalHashBytes);

        return AuthorityCrypto.VerifyHashedPayload(authority.PublicKeyBytes, payloadHash, result.SignatureBytes);
    }

    /// <summary>
    /// Verifies a <see cref="SignedRunResult"/> by replaying the session from
    /// <paramref name="replayInitialSnapshotJson"/> and <paramref name="replayTurns"/>,
    /// then checking both the deterministic hash and the authority signature.
    /// </summary>
    /// <remarks>
    /// This constitutes the full authority validation pipeline:
    /// <list type="number">
    ///   <item>Replay the session deterministically.</item>
    ///   <item>Compute the hash of the replayed final state.</item>
    ///   <item>Reject if the replay hash does not match <see cref="SignedRunResult.FinalHash"/>.</item>
    ///   <item>Verify the Ed25519 signature using the authority's public key.</item>
    /// </list>
    /// </remarks>
    public static bool VerifySignedRunWithReplay(
        SignedRunResult result,
        Authority authority,
        string replayInitialSnapshotJson,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(replayInitialSnapshotJson);
        ArgumentNullException.ThrowIfNull(replayTurns);

        // Step 1: Replay the session deterministically.
        var replayState = ReplayRunner.Run(replayInitialSnapshotJson, replayTurns);

        // Step 2: Compute hash from the replayed final snapshot.
        var replayHashBytes = CombatSessionHasher.ComputeStateHash(replayState.Snapshot);
        var replayHashHex = Convert.ToHexString(replayHashBytes);

        // Step 3: Reject if the replay hash does not match the signed hash.
        if (!string.Equals(replayHashHex, result.FinalHash, StringComparison.OrdinalIgnoreCase))
            return false;

        // Step 4: Verify the authority signature.
        return VerifySignedRun(result, authority);
    }

    /// <summary>
    /// Generates a new random Ed25519 private key suitable for use with <see cref="SessionAuthority"/>.
    /// </summary>
    public static byte[] GeneratePrivateKey() => AuthorityCrypto.GeneratePrivateKey();
}
