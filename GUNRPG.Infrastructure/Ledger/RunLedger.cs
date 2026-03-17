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

    public RunLedger(IEnumerable<Authority>? bootstrapAuthorities = null)
    {
        _readOnlyEntries = _entries.AsReadOnly();
        _merkleSkipIndex = new MerkleSkipIndex(GetEntryHashAt);
        _bootstrapAuthorityState = bootstrapAuthorities is null
            ? new AuthorityState(Array.Empty<Authority>())
            : new AuthorityState(bootstrapAuthorities);
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

    [Obsolete("Use the TryAppendWithQuorum overload that accepts a SignatureVerifier.")]
    public bool TryAppendWithQuorum(
        RunValidationResult run,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy)
    {
        throw new NotSupportedException("Use the TryAppendWithQuorum overload that accepts a SignatureVerifier.");
    }

    public bool TryAppendWithQuorum(
        RunValidationResult run,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendWithQuorum(run, signatureVerifier, quorumValidator, authoritySet, quorumPolicy, DateTimeOffset.UtcNow);
    }

    public bool TryAppendAuthorityEventWithQuorum(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy)
    {
        return TryAppendAuthorityEventWithQuorum(authorityEvent, signatures, quorumValidator, authoritySet, quorumPolicy, DateTimeOffset.UtcNow);
    }

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
        return entry;
    }

    internal bool TryAppendWithQuorum(
        RunValidationResult run,
        SignatureVerifier signatureVerifier,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(signatureVerifier);
        ArgumentNullException.ThrowIfNull(quorumValidator);
        ArgumentNullException.ThrowIfNull(authoritySet);
        ArgumentNullException.ThrowIfNull(quorumPolicy);

        PruneValidationCache(timestamp);

        var currentAuthorities = AuthorityState.BuildFromLedger(this, authoritySet);
        var resultHashKey = Convert.ToBase64String(run.Attestation.ResultHash);
        var mergedAttestation = run.Attestation;
        if (_validationCache.TryGetValue(resultHashKey, out var cachedValidation))
        {
            mergedAttestation = SignedRunValidation.Merge(cachedValidation.Validation, run.Attestation);
            if (ReferenceEquals(mergedAttestation, cachedValidation.Validation))
            {
                mergedAttestation = run.Attestation;
            }
        }

        var sanitizedAttestation = FilterTrustedValidSignatures(mergedAttestation, currentAuthorities);
        var candidateRun = ReferenceEquals(sanitizedAttestation, run.Attestation)
            ? run
            : new RunValidationResult(run.RunId, run.PlayerId, run.ServerId, run.FinalStateHash, sanitizedAttestation);

        if (!signatureVerifier.Verify(candidateRun.Attestation, timestamp))
        {
            return false;
        }

        _validationCache[resultHashKey] = new CachedValidation(sanitizedAttestation, timestamp);
        TrimValidationCacheIfNeeded();

        if (!quorumValidator.HasQuorum(candidateRun.Attestation, currentAuthorities, quorumPolicy))
        {
            return false;
        }

        _validationCache.Remove(resultHashKey);
        Append(candidateRun, timestamp);
        return true;
    }

    internal bool TryAppendAuthorityEventWithQuorum(
        AuthorityEvent authorityEvent,
        IEnumerable<AuthoritySignature> signatures,
        QuorumValidator quorumValidator,
        AuthoritySet authoritySet,
        QuorumPolicy quorumPolicy,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);
        ArgumentNullException.ThrowIfNull(quorumValidator);
        ArgumentNullException.ThrowIfNull(authoritySet);
        ArgumentNullException.ThrowIfNull(quorumPolicy);

        var currentAuthorities = AuthorityState.BuildFromLedger(this, authoritySet);
        if (!CanApplyAuthorityEvent(currentAuthorities, authorityEvent, quorumPolicy))
        {
            return false;
        }

        var normalizedSignatures = NormalizeAuthoritySignatures(signatures);
        var excludedSignerPublicKey = authorityEvent is AuthorityAdded added ? added.PublicKeyBytes : null;
        var eventHash = AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent);
        if (!quorumValidator.HasQuorum(normalizedSignatures, eventHash, currentAuthorities, quorumPolicy, excludedSignerPublicKey))
        {
            return false;
        }

        Append(authorityEvent, normalizedSignatures, timestamp);
        return true;
    }

    public bool Verify()
    {
        return VerifyEntries(_entries);
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

        _entries.Add(entry);
        _merkleSkipIndex.Append(entry);
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
    }

    internal AuthorityState GetBootstrapAuthorityState(AuthoritySet? fallbackBootstrapAuthorities)
    {
        if (_bootstrapAuthorityState.Count > 0)
        {
            return _bootstrapAuthorityState.Clone();
        }

        return fallbackBootstrapAuthorities is null
            ? new AuthorityState(Array.Empty<Authority>())
            : new AuthorityState(fallbackBootstrapAuthorities.KeyIdentifiers);
    }

    internal static bool VerifyEntries(IReadOnlyList<RunLedgerEntry> entries)
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

    private static ImmutableArray<byte> ComputeRunEntryHash(
        long index,
        ImmutableArray<byte> previousHash,
        DateTimeOffset timestamp,
        RunValidationResult run)
    {
        // Fixed-width payload: int64 + 32 bytes + int64 + 3×16 bytes + 32 bytes = 144 bytes
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
        var orderedSignatures = normalizedSignatures
            .OrderBy(static signature => AuthoritySet.CreateKeyIdentifier(signature.PublicKeyBytes), StringComparer.Ordinal)
            .ThenBy(static signature => Convert.ToHexString(signature.SignatureBytes), StringComparer.Ordinal)
            .ToArray();
        var eventHash = AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent);
        var buffer = new byte[Int64Size + HashSize + Int64Size + HashSize + Int32Size + (orderedSignatures.Length * (AuthorityCrypto.KeySize + AuthorityCrypto.SignatureSize))];
        var offset = 0;

        WriteInt64(index, buffer, ref offset);
        WriteBytes(previousHash.AsSpan(), buffer, ref offset);
        WriteInt64(timestamp.UtcTicks, buffer, ref offset);
        WriteBytes(eventHash, buffer, ref offset);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), orderedSignatures.Length);
        offset += Int32Size;

        foreach (var signature in orderedSignatures)
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

            var signerId = Convert.ToBase64String(signature.PublicKeyBytes);
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

        return new SignedRunValidation(validation.Validation, validation.Certificate)
        {
            Signatures = trustedValidSignatures
        };
    }

    private static ImmutableArray<AuthoritySignature> NormalizeAuthoritySignatures(IEnumerable<AuthoritySignature> signatures)
    {
        ArgumentNullException.ThrowIfNull(signatures);

        var normalized = new List<AuthoritySignature>();
        foreach (var signature in signatures)
        {
            normalized.Add(signature ?? throw new ArgumentException("Authority signature collections must not contain null entries.", nameof(signatures)));
        }

        return [.. normalized];
    }

    private static bool HasSupportedPayload(RunLedgerEntry entry)
    {
        return entry.Run is not null && entry.AuthoritySignatures.IsDefaultOrEmpty
            || entry.AuthorityEvent is not null;
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
