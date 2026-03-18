using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using GUNRPG.Gossip;
using GUNRPG.Ledger.Indexing;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public class RunLedger
{
    private const int HashSize = SHA256.HashSizeInBytes;
    private const int GuidSize = 16;
    private const int Int64Size = 8;
    private const int Int32Size = 4;
    private const int MaxValidationCacheEntries = 512;
    private static readonly TimeSpan ValidationCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly ImmutableArray<byte> ZeroHash = ImmutableArray.Create(new byte[HashSize]);

    private readonly List<RunLedgerEntry> _entries = [];
    private readonly ReadOnlyCollection<RunLedgerEntry> _readOnlyEntries;
    private readonly MerkleSkipIndex _merkleSkipIndex;
    private readonly Dictionary<string, CachedValidation> _validationCache = new(StringComparer.Ordinal);
    private readonly AuthorityState _bootstrapAuthorityState;
    private AuthorityState _currentAuthorityState;
    private QuorumPolicy? _configuredQuorumPolicy;

    public RunLedger(
        IEnumerable<Authority>? bootstrapAuthorities = null,
        QuorumPolicy? quorumPolicy = null)
    {
        _readOnlyEntries = _entries.AsReadOnly();
        _merkleSkipIndex = new MerkleSkipIndex(GetEntryHashAt);
        _bootstrapAuthorityState = bootstrapAuthorities is null
            ? new AuthorityState(Array.Empty<Authority>())
            : new AuthorityState(bootstrapAuthorities);
        _currentAuthorityState = _bootstrapAuthorityState.Clone();
        _configuredQuorumPolicy = quorumPolicy;
    }

    public IReadOnlyList<RunLedgerEntry> Entries => _readOnlyEntries;

    public RunLedgerEntry? Head => _entries.Count == 0 ? null : _entries[^1];

    public MerkleSkipIndex MerkleSkipIndex => _merkleSkipIndex;

    public RunLedgerEntry Append(RunValidationResult run)
    {
        return Append(run, DateTimeOffset.UtcNow);
    }

    public RunLedgerEntry Append(AuthorityEvent authorityEvent, IEnumerable<AuthoritySignature> signatures)
    {
        return Append(authorityEvent, signatures, DateTimeOffset.UtcNow);
    }

    [Obsolete("Use the overload that does not accept a bootstrap authority set.")]
    public bool TryAppendWithQuorum(
        RunValidationResult run,
        QuorumValidator quorumValidator,
        BootstrapAuthoritySet bootstrapAuthoritySet,
        QuorumPolicy quorumPolicy)
    {
        throw new NotSupportedException("Use the TryAppendWithQuorum overload that accepts a SignatureVerifier.");
    }

    [Obsolete("Bootstrap authority sets are only used during ledger initialization.")]
    public bool TryAppendWithQuorum(
        RunValidationResult run,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        BootstrapAuthoritySet bootstrapAuthoritySet,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendWithQuorum(run, signatureVerifier, quorumValidator, quorumPolicy);
    }

    public bool TryAppendWithQuorum(
        RunValidationResult run,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendWithQuorum(run, signatureVerifier, quorumValidator, quorumPolicy, DateTimeOffset.UtcNow);
    }

    public bool TryAppendWithReplayValidation(
        RunInput input,
        RunValidationResult? submittedResult,
        IRunReplayEngine replayEngine,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendWithReplayValidation(
            input,
            submittedResult,
            replayEngine,
            signatureVerifier,
            quorumValidator,
            quorumPolicy,
            DateTimeOffset.UtcNow);
    }

    [Obsolete("Bootstrap authority sets are only used during ledger initialization.")]
    public bool TryAppendAuthorityEventWithQuorum(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        QuorumValidator quorumValidator,
        BootstrapAuthoritySet bootstrapAuthoritySet,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendAuthorityEventWithQuorum(authorityEvent, signatures, quorumValidator, quorumPolicy);
    }

    public bool TryAppendAuthorityEventWithQuorum(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendAuthorityEventWithQuorum(authorityEvent, signatures, quorumValidator, quorumPolicy, DateTimeOffset.UtcNow);
    }

    internal AuthorityState CurrentAuthorityState => _currentAuthorityState.Clone();

    internal RunLedgerEntry Append(RunValidationResult run, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);

        var index = (long)_entries.Count;
        var previousHash = _entries.Count == 0 ? ZeroHash : _entries[^1].EntryHash;
        var entryHash = ComputeEntryHash(index, previousHash, timestamp, run, authoritySignatures: []);

        var entry = new RunLedgerEntry(index, previousHash, entryHash, timestamp, run);
        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
        return entry;
    }

    internal RunLedgerEntry Append(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);

        var normalizedSignatures = NormalizeAuthoritySignatures(signatures);
        var index = (long)_entries.Count;
        var previousHash = _entries.Count == 0 ? ZeroHash : _entries[^1].EntryHash;
        var entryHash = ComputeEntryHash(index, previousHash, timestamp, authorityEvent, normalizedSignatures);

        var entry = new RunLedgerEntry(index, previousHash, entryHash, timestamp, authorityEvent, normalizedSignatures);
        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
        _currentAuthorityState = _currentAuthorityState.Apply(authorityEvent);
        return entry;
    }

    internal bool TryAppendWithQuorum(
        RunValidationResult run,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(signatureVerifier);
        ArgumentNullException.ThrowIfNull(quorumValidator);
        ArgumentNullException.ThrowIfNull(quorumPolicy);

        if (!TryConfigureQuorumPolicy(quorumPolicy))
        {
            return false;
        }

        PruneValidationCache(timestamp);

        var resultHashKey = Convert.ToBase64String(run.Attestation.ResultHash);
        var mergedAttestation = run.Attestation;
        if (_validationCache.TryGetValue(resultHashKey, out var cachedValidation))
        {
            mergedAttestation = SignedRunValidation.MergeSignatures(cachedValidation.Validation, run.Attestation);
            if (ReferenceEquals(mergedAttestation, cachedValidation.Validation))
            {
                mergedAttestation = run.Attestation;
            }
        }

        var sanitizedAttestation = FilterTrustedValidSignatures(mergedAttestation, _currentAuthorityState);
        var candidateRun = ReferenceEquals(sanitizedAttestation, run.Attestation)
            ? run
            : new RunValidationResult(run.RunId, run.PlayerId, run.ServerId, run.FinalStateHash, sanitizedAttestation, run.Mutation);

        if (!signatureVerifier.Verify(candidateRun.Attestation, timestamp))
        {
            return false;
        }

        _validationCache[resultHashKey] = new CachedValidation(sanitizedAttestation, timestamp);
        TrimValidationCacheIfNeeded();

        if (!quorumValidator.HasQuorum(candidateRun.Attestation, _currentAuthorityState, quorumPolicy))
        {
            return false;
        }

        _validationCache.Remove(resultHashKey);
        Append(candidateRun, timestamp);
        return true;
    }

    internal bool TryAppendWithReplayValidation(
        RunInput input,
        RunValidationResult? submittedResult,
        IRunReplayEngine replayEngine,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(replayEngine);

        // Independently replay from Actions + Seed to get the authoritative GameplayLedgerEvents.
        var authoritativeResult = replayEngine.Replay(input);
        if (submittedResult is not null && !HasMatchingResult(authoritativeResult, submittedResult))
        {
            return false;
        }

        // Preserve server-supplied OperatorEvents from the submitted mutation (if any) so that
        // ledger-projection queries can reconstruct operator state.  Only the FinalStateHash
        // (which covers Actions + Seed + derived gameplay events) is validated above; the
        // OperatorEvents are server-internal and are never accepted via RunInput.
        //
        // For GameplayLedgerEvents: prefer the authoritative replay-derived events when
        // non-empty; fall back to the bridge-supplied semantic events so that ledger entries
        // always carry gameplay history while the service is still transitioning to full
        // action-based replay.
        RunValidationResult resultToStore;
        if (submittedResult is not null && submittedResult.Mutation.OperatorEvents.Count > 0)
        {
            var gameplayEvents = authoritativeResult.Events.Count > 0
                ? authoritativeResult.Events
                : submittedResult.Mutation.GameplayEvents;

            var mergedMutation = new RunLedgerMutation(
                submittedResult.Mutation.OperatorEvents,
                gameplayEvents);

            resultToStore = new RunValidationResult(
                authoritativeResult.RunId,
                authoritativeResult.PlayerId,
                authoritativeResult.ServerId,
                authoritativeResult.FinalStateHash,
                authoritativeResult.Attestation,
                mergedMutation);
        }
        else
        {
            resultToStore = authoritativeResult;
        }

        var replayValidatedResult = AttachAuthoritySignatures(resultToStore, submittedResult?.Attestation.Signatures);
        return TryAppendWithQuorum(replayValidatedResult, signatureVerifier, quorumValidator, quorumPolicy, timestamp);
    }

    internal bool TryAppendAuthorityEventWithQuorum(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        QuorumValidator quorumValidator,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);
        ArgumentNullException.ThrowIfNull(quorumValidator);
        ArgumentNullException.ThrowIfNull(quorumPolicy);

        if (!TryConfigureQuorumPolicy(quorumPolicy))
        {
            return false;
        }

        if (!CanApplyAuthorityEvent(_currentAuthorityState, authorityEvent, quorumPolicy))
        {
            return false;
        }

        var normalizedSignatures = NormalizeAuthoritySignatures(signatures);
        var excludedSignerPublicKey = authorityEvent is AuthorityAdded added ? added.PublicKeyBytes : null;
        var eventHash = AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent);
        if (!quorumValidator.HasQuorum(normalizedSignatures, eventHash, _currentAuthorityState, quorumPolicy, excludedSignerPublicKey))
        {
            return false;
        }

        Append(authorityEvent, normalizedSignatures, timestamp);
        return true;
    }

    public bool Verify()
    {
        return VerifyEntryChain(_entries, allowAuthorityEvents: true)
            && VerifyAuthorityStateTransitions(_entries, expectedFinalState: _currentAuthorityState);
    }

    public LedgerHead GetHead()
    {
        if (_entries.Count == 0)
        {
            return new LedgerHead(-1, ZeroHash);
        }

        var head = _entries[^1];
        return new LedgerHead(head.Index, head.EntryHash);
    }

    public IReadOnlyList<RunLedgerEntry> GetEntriesFrom(long fromIndex, int maxCount = int.MaxValue)
    {
        if (maxCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount must be non-negative.");
        }

        if (fromIndex < 0 || fromIndex >= _entries.Count)
        {
            return [];
        }

        var count = Math.Min(_entries.Count - (int)fromIndex, maxCount);
        return _entries.GetRange((int)fromIndex, count).AsReadOnly();
    }

    public bool TryAppendEntry(RunLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!HasSupportedPayload(entry))
        {
            return false;
        }

        if (entry.AuthorityEvent is not null && !HasCanonicalAuthoritySignatures(entry.AuthoritySignatures))
        {
            return false;
        }

        var expectedIndex = (long)_entries.Count;
        if (entry.Index != expectedIndex)
        {
            return false;
        }

        if (entry.EntryHash.Length != HashSize || entry.PreviousHash.Length != HashSize)
        {
            return false;
        }

        var expectedPreviousHash = _entries.Count == 0 ? ZeroHash : _entries[^1].EntryHash;
        if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash.AsSpan(), expectedPreviousHash.AsSpan()))
        {
            return false;
        }

        var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Payload, entry.AuthoritySignatures);
        if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash.AsSpan(), recomputed.AsSpan()))
        {
            return false;
        }

        if (entry.AuthorityEvent is not null && !CanAppendVerifiedAuthorityEntry(entry.AuthorityEvent, entry.AuthoritySignatures))
        {
            return false;
        }

        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
        if (entry.AuthorityEvent is not null)
        {
            _currentAuthorityState = _currentAuthorityState.Apply(entry.AuthorityEvent);
        }

        return true;
    }

    // Replaces an entry at the given index — internal for tamper-detection testing only.
    internal void ReplaceEntryForTest(int index, RunLedgerEntry entry)
    {
        if (entry.Index != index)
        {
            throw new ArgumentException("Replacement entry index must match the target index.", nameof(entry));
        }

        _entries[index] = entry;
        _merkleSkipIndex.Update(entry);
        RefreshAuthorityStateCache();
    }

    internal void ReplaceEntriesFrom(long divergenceIndex, IReadOnlyList<RunLedgerEntry> entries, int startIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (divergenceIndex < 0 || divergenceIndex > _entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(divergenceIndex));
        }

        if (startIndex < 0 || startIndex > entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        _entries.RemoveRange((int)divergenceIndex, _entries.Count - (int)divergenceIndex);
        for (var i = startIndex; i < entries.Count; i++)
        {
            _entries.Add(entries[i]);
        }

        _merkleSkipIndex.Rebuild(_entries);
        RefreshAuthorityStateCache();
    }

    internal AuthorityState GetBootstrapAuthorityState()
    {
        return _bootstrapAuthorityState.Clone();
    }

    internal static bool VerifyEntries(IReadOnlyList<RunLedgerEntry> entries)
    {
        return VerifyEntryChain(entries, allowAuthorityEvents: false);
    }

    internal static ImmutableArray<byte> ComputeEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        object payload,
        ImmutableArray<AuthoritySignature> authoritySignatures)
    {
        return payload switch
        {
            RunValidationResult run when authoritySignatures.IsDefaultOrEmpty
                => ComputeRunEntryHash(index, previousHash, timestamp, run),
            AuthorityEvent authorityEvent
                => ComputeAuthorityEntryHash(index, previousHash, timestamp, authorityEvent, authoritySignatures.IsDefault ? [] : authoritySignatures),
            RunValidationResult => throw new ArgumentException("Run validation entries must not carry authority event signatures.", nameof(authoritySignatures)),
            _ => throw new ArgumentException("Unsupported ledger payload type.", nameof(payload))
        };
    }

    internal static ImmutableArray<byte> ComputeEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        return ComputeRunEntryHash(index, previousHash, timestamp, run);
    }

    internal bool VerifyEntriesForCurrentConfiguration(IReadOnlyList<RunLedgerEntry> entries)
    {
        return VerifyEntryChain(entries, allowAuthorityEvents: true)
            && VerifyAuthorityStateTransitions(entries, expectedFinalState: null);
    }

    private bool VerifyAuthorityStateTransitions(
        IReadOnlyList<RunLedgerEntry> entries,
        AuthorityState? expectedFinalState)
    {
        if (entries.All(static entry => entry.AuthorityEvent is null))
        {
            return true;
        }

        if (_configuredQuorumPolicy is null)
        {
            return false;
        }

        var authorityState = _bootstrapAuthorityState.Clone();
        var quorumValidator = new QuorumValidator();

        foreach (var entry in entries)
        {
            if (entry.AuthorityEvent is null)
            {
                continue;
            }

            if (!CanApplyAuthorityEvent(authorityState, entry.AuthorityEvent, _configuredQuorumPolicy))
            {
                return false;
            }

            var excludedSignerPublicKey = entry.AuthorityEvent is AuthorityAdded added ? added.PublicKeyBytes : null;
            if (!quorumValidator.HasQuorum(
                    entry.AuthoritySignatures,
                    AuthorityCrypto.ComputeAuthorityEventHash(entry.AuthorityEvent),
                    authorityState,
                    _configuredQuorumPolicy,
                    excludedSignerPublicKey))
            {
                return false;
            }

            authorityState = authorityState.Apply(entry.AuthorityEvent);
        }

        return expectedFinalState is null
            || (authorityState.Count == expectedFinalState.Count
                && authorityState.IsEquivalentTo(expectedFinalState));
    }

    private static ImmutableArray<byte> ComputeRunEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        var buffer = new byte[Int64Size + HashSize + Int64Size + GuidSize + GuidSize + GuidSize + HashSize];
        var offset = 0;

        WriteInt64(index, buffer, ref offset);
        WriteBytes(previousHash.AsSpan(), buffer, ref offset);
        WriteInt64(timestamp.UtcTicks, buffer, ref offset);
        WriteGuid(run.RunId, buffer, ref offset);
        WriteGuid(run.PlayerId, buffer, ref offset);
        WriteGuid(run.ServerId, buffer, ref offset);
        WriteBytes(run.FinalStateHash, buffer, ref offset);

        return ImmutableArray.Create(SHA256.HashData(buffer));
    }

    private static ImmutableArray<byte> ComputeAuthorityEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        AuthorityEvent authorityEvent,
        ImmutableArray<AuthoritySignature> authoritySignatures)
    {
        var normalizedSignatures = NormalizeAuthoritySignatures(authoritySignatures);
        var eventHash = AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent);
        var buffer = new byte[Int64Size + HashSize + Int64Size + HashSize + Int32Size + (normalizedSignatures.Length * (AuthorityCrypto.KeySize + AuthorityCrypto.SignatureSize))];
        var offset = 0;

        WriteInt64(index, buffer, ref offset);
        WriteBytes(previousHash.AsSpan(), buffer, ref offset);
        WriteInt64(timestamp.UtcTicks, buffer, ref offset);
        WriteBytes(eventHash, buffer, ref offset);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), normalizedSignatures.Length);
        offset += Int32Size;

        foreach (var signature in normalizedSignatures)
        {
            WriteBytes(signature.PublicKeyBytes, buffer, ref offset);
            WriteBytes(signature.SignatureBytes, buffer, ref offset);
        }

        return ImmutableArray.Create(SHA256.HashData(buffer));
    }

    private static void WriteInt64(long value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += Int64Size;
    }

    private static void WriteBytes(ReadOnlySpan<byte> value, Span<byte> destination, ref int offset)
    {
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static void WriteGuid(Guid value, Span<byte> destination, ref int offset)
    {
        if (!value.TryWriteBytes(destination[offset..], bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException("Failed to write a 16-byte big-endian Guid into the ledger entry hash buffer.");
        }

        offset += bytesWritten;
    }

    private ImmutableArray<byte>? GetEntryHashAt(long index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return null;
        }

        return _entries[(int)index].EntryHash;
    }

    private static SignedRunValidation FilterTrustedValidSignatures(
        SignedRunValidation validation,
        AuthorityState authorityState)
    {
        var trustedValidSignatures = new List<AuthoritySignature>();
        var seenSigners = new HashSet<string>(StringComparer.Ordinal);
        var resultHash = validation.ResultHash;

        foreach (var signature in validation.Signatures)
        {
            if (signature is null || !authorityState.IsTrusted(signature.PublicKeyBytes))
            {
                continue;
            }

            var signerId = AuthoritySignatureOrdering.CreateSignerId(signature.PublicKeyBytes);
            if (!seenSigners.Add(signerId))
            {
                continue;
            }

            if (!AuthorityCrypto.VerifyHashedPayload(signature.PublicKeyBytes, resultHash, signature.SignatureBytes))
            {
                continue;
            }

            trustedValidSignatures.Add(signature);
        }

        var normalizedSignatures = NormalizeAuthoritySignatures(trustedValidSignatures);
        return new SignedRunValidation(validation.Validation, validation.Certificate)
        {
            Signatures = [.. normalizedSignatures]
        };
    }

    private static bool HasMatchingResult(RunValidationResult authoritativeResult, RunValidationResult submittedResult)
    {
        var authoritativeHash = authoritativeResult.Attestation.ResultHash;
        var submittedHash = submittedResult.Attestation.ResultHash;
        return CryptographicOperations.FixedTimeEquals(authoritativeHash, submittedHash);
    }

    private static RunValidationResult AttachAuthoritySignatures(
        RunValidationResult authoritativeResult,
        IEnumerable<AuthoritySignature>? signatures)
    {
        if (signatures is null)
        {
            return authoritativeResult;
        }

        var authoritativeAttestation = new SignedRunValidation(
            authoritativeResult.Attestation.Validation,
            authoritativeResult.Attestation.Certificate)
        {
            Signatures = [.. signatures]
        };

        return new RunValidationResult(
            authoritativeResult.RunId,
            authoritativeResult.PlayerId,
            authoritativeResult.ServerId,
            authoritativeResult.FinalStateHash,
            authoritativeAttestation,
            authoritativeResult.Mutation);
    }

    private static ImmutableArray<AuthoritySignature> NormalizeAuthoritySignatures(IEnumerable<AuthoritySignature> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        var ordered = new List<(AuthoritySignature Signature, string SignerId)>();
        foreach (var signature in signatures)
        {
            var normalizedSignature = signature ?? throw new ArgumentException("Authority signature collections must not contain null entries.", nameof(signatures));
            ordered.Add((
                normalizedSignature,
                AuthoritySignatureOrdering.CreateSignerId(normalizedSignature.PublicKeyBytes)));
        }

        ordered.Sort(static (left, right) =>
        {
            return AuthoritySignatureOrdering.Compare(
                left.SignerId,
                left.Signature.SignatureBytes,
                right.SignerId,
                right.Signature.SignatureBytes);
        });

        var seenSigners = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<AuthoritySignature>(ordered.Count);
        foreach (var item in ordered)
        {
            if (!seenSigners.Add(item.SignerId))
            {
                continue;
            }

            normalized.Add(item.Signature);
        }

        return [.. normalized];
    }

    private static bool HasCanonicalAuthoritySignatures(ImmutableArray<AuthoritySignature> signatures)
    {
        if (signatures.IsDefault)
        {
            return false;
        }

        var normalized = NormalizeAuthoritySignatures(signatures);
        if (normalized.Length != signatures.Length)
        {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++)
        {
            if (!CryptographicOperations.FixedTimeEquals(normalized[i].PublicKeyBytes, signatures[i].PublicKeyBytes)
                || !CryptographicOperations.FixedTimeEquals(normalized[i].SignatureBytes, signatures[i].SignatureBytes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSupportedPayload(RunLedgerEntry entry)
    {
        return entry.Run is not null && entry.AuthoritySignatures.IsDefaultOrEmpty
            || (entry.AuthorityEvent is not null && !entry.AuthoritySignatures.IsDefault);
    }

    private bool CanAppendVerifiedAuthorityEntry(
        AuthorityEvent authorityEvent,
        ImmutableArray<AuthoritySignature> authoritySignatures)
    {
        if (_configuredQuorumPolicy is null)
        {
            return false;
        }

        if (!CanApplyAuthorityEvent(_currentAuthorityState, authorityEvent, _configuredQuorumPolicy))
        {
            return false;
        }

        var excludedSignerPublicKey = authorityEvent is AuthorityAdded added ? added.PublicKeyBytes : null;
        return new QuorumValidator().HasQuorum(
            authoritySignatures,
            AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent),
            _currentAuthorityState,
            _configuredQuorumPolicy,
            excludedSignerPublicKey);
    }

    private static bool VerifyEntryChain(
        IReadOnlyList<RunLedgerEntry> entries,
        bool allowAuthorityEvents)
    {
        ArgumentNullException.ThrowIfNull(entries);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            if (entry.Index != i)
            {
                return false;
            }

            if (!HasSupportedPayload(entry) || entry.EntryHash.Length != HashSize || entry.PreviousHash.Length != HashSize)
            {
                return false;
            }

            if (entry.AuthorityEvent is not null)
            {
                if (!allowAuthorityEvents || !HasCanonicalAuthoritySignatures(entry.AuthoritySignatures))
                {
                    return false;
                }
            }

            var recomputed = ComputeEntryHash(entry.Index, entry.PreviousHash, entry.Timestamp, entry.Payload, entry.AuthoritySignatures);
            if (!CryptographicOperations.FixedTimeEquals(entry.EntryHash.AsSpan(), recomputed.AsSpan()))
            {
                return false;
            }

            var expectedPreviousHash = i == 0 ? ZeroHash : entries[i - 1].EntryHash;
            if (expectedPreviousHash.Length != HashSize)
            {
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(entry.PreviousHash.AsSpan(), expectedPreviousHash.AsSpan()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanApplyAuthorityEvent(
        AuthorityState currentAuthorities,
        AuthorityEvent authorityEvent,
        QuorumPolicy quorumPolicy)
    {
        return authorityEvent switch
        {
            AuthorityAdded added => !currentAuthorities.IsTrusted(added.PublicKeyBytes),
            AuthorityRemoved removed => currentAuthorities.IsTrusted(removed.PublicKeyBytes)
                && currentAuthorities.Apply(authorityEvent).Count >= quorumPolicy.RequiredSignatures,
            AuthorityRotated rotated => !CryptographicOperations.FixedTimeEquals(rotated.OldKeyBytes, rotated.NewKeyBytes)
                && currentAuthorities.IsTrusted(rotated.OldKeyBytes)
                && !currentAuthorities.IsTrusted(rotated.NewKeyBytes)
                && currentAuthorities.Apply(authorityEvent).Count >= quorumPolicy.RequiredSignatures,
            _ => false
        };
    }

    private void RefreshAuthorityStateCache()
    {
        _currentAuthorityState = AuthorityState.BuildFromLedger(this);
    }

    private bool TryConfigureQuorumPolicy(QuorumPolicy quorumPolicy)
    {
        if (_configuredQuorumPolicy is null)
        {
            _configuredQuorumPolicy = quorumPolicy;
            return true;
        }

        return _configuredQuorumPolicy.RequiredSignatures == quorumPolicy.RequiredSignatures;
    }

    private void PruneValidationCache(DateTimeOffset now)
    {
        if (_validationCache.Count == 0)
        {
            return;
        }

        List<string>? expiredKeys = null;
        foreach (var cacheEntry in _validationCache)
        {
            var age = now - cacheEntry.Value.CachedAt;
            if (age > ValidationCacheTtl || cacheEntry.Value.Validation.Certificate.ValidUntil <= now)
            {
                expiredKeys ??= [];
                expiredKeys.Add(cacheEntry.Key);
            }
        }

        if (expiredKeys is null)
        {
            return;
        }

        foreach (var key in expiredKeys)
        {
            _validationCache.Remove(key);
        }
    }

    private void TrimValidationCacheIfNeeded()
    {
        var overflow = _validationCache.Count - MaxValidationCacheEntries;
        if (overflow <= 0)
        {
            return;
        }

        var cacheEntries = new List<KeyValuePair<string, CachedValidation>>(_validationCache);
        cacheEntries.Sort(static (left, right) => left.Value.CachedAt.CompareTo(right.Value.CachedAt));

        for (var i = 0; i < overflow && i < cacheEntries.Count; i++)
        {
            _validationCache.Remove(cacheEntries[i].Key);
        }
    }

    private sealed record CachedValidation(SignedRunValidation Validation, DateTimeOffset CachedAt);
}
