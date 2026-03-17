using System.Collections.Immutable;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public sealed record RunLedgerEntry
{
    public RunLedgerEntry(
        long index,
        ImmutableArray<byte> previousHash,
        ImmutableArray<byte> entryHash,
        DateTimeOffset timestamp,
        object payload,
        ImmutableArray<AuthoritySignature> authoritySignatures = default)
    {
        if (payload is not RunValidationResult && payload is not GUNRPG.Security.AuthorityEvent)
        {
            throw new ArgumentException("Payload must be a run validation result or authority event.", nameof(payload));
        }

        if (payload is RunValidationResult && !authoritySignatures.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Run validation entries must not carry authority event signatures.", nameof(authoritySignatures));
        }

        if (!authoritySignatures.IsDefault)
        {
            foreach (var signature in authoritySignatures)
            {
                ArgumentNullException.ThrowIfNull(signature);
            }
        }

        Index = index;
        PreviousHash = previousHash;
        EntryHash = entryHash;
        Timestamp = timestamp;
        Payload = payload;
        AuthoritySignatures = authoritySignatures.IsDefault ? [] : authoritySignatures;
    }

    public long Index { get; init; }

    public ImmutableArray<byte> PreviousHash { get; init; }

    public ImmutableArray<byte> EntryHash { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public object Payload { get; init; }

    public ImmutableArray<AuthoritySignature> AuthoritySignatures { get; init; }

    public RunValidationResult? Run => Payload as RunValidationResult;

    public GUNRPG.Security.AuthorityEvent? AuthorityEvent => Payload as GUNRPG.Security.AuthorityEvent;
}
