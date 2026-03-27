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
        string? tickMerkleRoot = null,
        IReadOnlyList<RunCheckpoint>? checkpoints = null)
    {
        if (string.IsNullOrWhiteSpace(finalHash))
            throw new ArgumentException("FinalHash must not be empty.", nameof(finalHash));
        if (finalHash.Length != FinalHashHexLength)
            throw new ArgumentException(
                $"FinalHash must be a {FinalHashHexLength}-character uppercase hex string (SHA-256).",
                nameof(finalHash));
        if (!IsValidHex(finalHash))
            throw new ArgumentException(
                "FinalHash must contain only hexadecimal characters (0-9, A-F).",
                nameof(finalHash));
        if (string.IsNullOrWhiteSpace(authorityId))
            throw new ArgumentException("AuthorityId must not be empty.", nameof(authorityId));
        if (replayHash is not null)
        {
            if (replayHash.Length != FinalHashHexLength)
                throw new ArgumentException(
                    $"ReplayHash must be a {FinalHashHexLength}-character uppercase hex string (SHA-256) when provided.",
                    nameof(replayHash));
            if (!IsValidHex(replayHash))
                throw new ArgumentException(
                    "ReplayHash must contain only hexadecimal characters (0-9, A-F).",
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
            if (!IsValidHex(tickMerkleRoot))
                throw new ArgumentException(
                    "TickMerkleRoot must contain only hexadecimal characters (0-9, A-F).",
                    nameof(tickMerkleRoot));
        }

        if (checkpoints is not null)
        {
            if (tickMerkleRoot is null)
                throw new ArgumentException(
                    "Checkpoints can only be provided when TickMerkleRoot is also provided.",
                    nameof(checkpoints));
            var prevTickIndex = -1L;
            for (var i = 0; i < checkpoints.Count; i++)
            {
                var cp = checkpoints[i];
                if (cp is null)
                    throw new ArgumentException(
                        $"Checkpoint at index {i} must not be null.", nameof(checkpoints));
                if (cp.TickIndex <= prevTickIndex)
                    throw new ArgumentException(
                        $"Checkpoints must have strictly increasing TickIndex values. " +
                        $"Checkpoint at index {i} has TickIndex {cp.TickIndex}, " +
                        $"which is not greater than the previous TickIndex {prevTickIndex}.",
                        nameof(checkpoints));
                if (cp.StateHash is null || cp.StateHash.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
                    throw new ArgumentException(
                        $"Checkpoint at index {i} has an invalid StateHash " +
                        $"(must be {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes).",
                        nameof(checkpoints));
                prevTickIndex = cp.TickIndex;
            }
        }

        SessionId = sessionId;
        PlayerId = playerId;
        FinalHash = finalHash;
        AuthorityId = authorityId;
        ReplayHash = replayHash;
        TickMerkleRoot = tickMerkleRoot;
        _signature = AuthorityCrypto.CloneAndValidateSignature(signature);

        // Deep-copy each checkpoint so the stored list and its StateHash arrays are
        // independent from the caller's original data. Note that the individual StateHash
        // byte arrays inside the stored RunCheckpoints are still mutable by readers.
        if (checkpoints is not null)
        {
            var safe = new List<RunCheckpoint>(checkpoints.Count);
            foreach (var cp in checkpoints)
                safe.Add(new RunCheckpoint(cp.TickIndex, (byte[])cp.StateHash.Clone()));
            Checkpoints = safe.AsReadOnly();
        }
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

    /// <summary>
    /// Merkle root of all tick leaf hashes in the run, encoded as an uppercase hex string.
    /// Present only when the result was signed with the Merkle overload
    /// (<see cref="SessionAuthority.Sign(Guid, Guid, byte[], byte[], byte[])"/>).
    /// When present the signature covers <see cref="FinalHash"/>, <see cref="ReplayHash"/>, and this value.
    /// </summary>
    public string? TickMerkleRoot { get; }

    /// <summary>
    /// Ordered list of Merkle checkpoints recorded at a fixed interval during simulation.
    /// Present only when the result was signed with the checkpoint overload
    /// (<see cref="SessionAuthority.Sign(Guid, Guid, byte[], byte[], byte[], IReadOnlyList{RunCheckpoint})"/>).
    /// When present the signature covers <see cref="FinalHash"/>, <see cref="ReplayHash"/>,
    /// <see cref="TickMerkleRoot"/>, and <c>Hash(Checkpoints)</c>.
    /// </summary>
    public IReadOnlyList<RunCheckpoint>? Checkpoints { get; }

    /// <summary>Ed25519 signature over the validation payload hash.</summary>
    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;

    private static bool IsValidHex(string value)
    {
        foreach (var c in value)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f')))
                return false;
        }
        return true;
    }
}
