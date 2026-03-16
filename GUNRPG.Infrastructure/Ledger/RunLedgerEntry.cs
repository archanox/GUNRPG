using GUNRPG.Security;

namespace GUNRPG.Ledger;

public record RunLedgerEntry(
    long Index,
    byte[] PreviousHash,
    byte[] EntryHash,
    DateTimeOffset Timestamp,
    RunValidationResult Run);
