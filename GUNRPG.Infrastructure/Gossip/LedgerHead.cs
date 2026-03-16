using System.Collections.Immutable;

namespace GUNRPG.Gossip;

public record LedgerHead(
    long Index,
    ImmutableArray<byte> EntryHash);
