using System.Collections.Immutable;
using GUNRPG.Security;

namespace GUNRPG.Ledger;

public record RunLedgerEntry(
    long Index,
    ImmutableArray<byte> PreviousHash,
    ImmutableArray<byte> EntryHash,
    DateTimeOffset Timestamp,
    RunValidationResult Run);
