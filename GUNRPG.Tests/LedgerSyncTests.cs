using System.Collections.Immutable;
using System.Security.Cryptography;
using GUNRPG.Core.Operators;
using GUNRPG.Gossip;
using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class LedgerSyncTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void LedgerSync_AppendsMissingEntries()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result3 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledgerA.Append(result1);
        ledgerA.Append(result2);
        ledgerA.Append(result3);

        var syncEngine = new LedgerSyncEngine(ledgerB);
        var peerHead = ledgerA.GetHead();

        Assert.True(syncEngine.NeedsSync(peerHead));

        var request = syncEngine.BuildSyncRequest(peerHead);
        Assert.Equal(0L, request.FromIndex);

        var entries = ledgerA.GetEntriesFrom(request.FromIndex, LedgerSyncEngine.MaxSyncBatchSize);
        var response = new LedgerSyncResponse(entries);

        var applied = syncEngine.ApplyResponse(response);

        Assert.True(applied);
        Assert.Equal(ledgerA.Entries.Count, ledgerB.Entries.Count);
        Assert.True(ledgerA.GetHead().EntryHash.SequenceEqual(ledgerB.GetHead().EntryHash));
        Assert.True(ledgerB.Verify());
    }

    [Fact]
    public void LedgerSync_RejectsInvalidEntry()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var entry = ledgerA.Append(result);

        var tamperedHash = ImmutableArray.Create(new byte[SHA256.HashSizeInBytes]);
        var tamperedEntry = entry with { EntryHash = tamperedHash };

        var ledgerB = new RunLedger();
        var syncEngine = new LedgerSyncEngine(ledgerB);

        var response = new LedgerSyncResponse([tamperedEntry]);
        var applied = syncEngine.ApplyResponse(response);

        Assert.False(applied);
        Assert.Empty(ledgerB.Entries);
    }

    [Fact]
    public void LedgerSync_NeedsSync_ReturnsFalseWhenUpToDate()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        ledgerA.Append(result);
        ledgerB.Append(result);

        var syncEngine = new LedgerSyncEngine(ledgerB);
        Assert.False(syncEngine.NeedsSync(ledgerA.GetHead()));
    }

    [Fact]
    public void LedgerSync_GetHead_ReturnsMinusOneForEmptyLedger()
    {
        var ledger = new RunLedger();
        var head = ledger.GetHead();

        Assert.Equal(-1L, head.Index);
        Assert.True(head.EntryHash.SequenceEqual(new byte[SHA256.HashSizeInBytes]));
    }

    [Fact]
    public void LedgerSync_GetEntriesFrom_ReturnsCorrectRange()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result3 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledger.Append(result1);
        ledger.Append(result2);
        ledger.Append(result3);

        var entries = ledger.GetEntriesFrom(1);
        Assert.Equal(2, entries.Count);
        Assert.Equal(1L, entries[0].Index);
        Assert.Equal(2L, entries[1].Index);
    }

    [Fact]
    public void LedgerSync_TryAppendEntry_RejectsWrongIndex()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var entry = ledgerA.Append(result);

        // Try appending an entry at index 0 twice — second attempt must fail due to wrong index
        var ledgerB = new RunLedger();
        Assert.True(ledgerB.TryAppendEntry(entry));
        Assert.False(ledgerB.TryAppendEntry(entry));
    }

    [Fact]
    public void LedgerSync_ConvergesAfterExchange()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledgerA.Append(result1);
        ledgerA.Append(result2);

        // Sync B from A
        var syncEngineB = new LedgerSyncEngine(ledgerB);
        var headA = ledgerA.GetHead();
        Assert.True(syncEngineB.NeedsSync(headA));

        var requestB = syncEngineB.BuildSyncRequest(headA);
        var entriesForB = ledgerA.GetEntriesFrom(requestB.FromIndex, LedgerSyncEngine.MaxSyncBatchSize);
        Assert.True(syncEngineB.ApplyResponse(new LedgerSyncResponse(entriesForB)));

        // A should not need sync from B (B has no additional data)
        var syncEngineA = new LedgerSyncEngine(ledgerA);
        var headB = ledgerB.GetHead();
        Assert.False(syncEngineA.NeedsSync(headB));

        // Both heads must now match
        Assert.True(syncEngineA.IsSameHead(headB));
        Assert.True(syncEngineB.IsSameHead(ledgerA.GetHead()));
    }

    [Fact]
    public void LedgerSync_DetectsFork()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        // Both ledgers append different runs at the same index
        var resultA = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var resultB = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledgerA.Append(resultA);
        ledgerB.Append(resultB);

        var syncEngine = new LedgerSyncEngine(ledgerB);
        var peerHead = ledgerA.GetHead();

        // Same index, different hash — fork detected; sync must be rejected
        Assert.False(syncEngine.NeedsSync(peerHead));
        Assert.False(syncEngine.IsSameHead(peerHead));
    }

    [Fact]
    public void ForkResolution_SelectsLongestChain()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        for (var i = 0; i < 8; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            var timestamp = ReferenceNow.AddMinutes(i);

            var entryA = ledgerA.Append(result, timestamp);
            var entryB = ledgerB.Append(result, timestamp);

            Assert.True(entryA.EntryHash.SequenceEqual(entryB.EntryHash));
        }

        for (var i = 0; i < 2; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            ledgerA.Append(result, ReferenceNow.AddMinutes(8 + i));
        }

        for (var i = 0; i < 5; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            ledgerB.Append(result, ReferenceNow.AddHours(1).AddMinutes(i));
        }

        Assert.Equal(8L, ledgerA.MerkleSkipIndex.FindDivergenceIndex(ledgerB.MerkleSkipIndex));

        var syncEngineA = new LedgerSyncEngine(ledgerA);
        var syncEngineB = new LedgerSyncEngine(ledgerB);

        Assert.True(syncEngineA.ResolveFork(ledgerA, ledgerB.Entries));
        Assert.False(syncEngineB.ResolveFork(ledgerB, ledgerA.Entries));
        Assert.Equal(ledgerA.Entries.Count, ledgerB.Entries.Count);
        Assert.True(syncEngineA.IsSameHead(ledgerB.GetHead()));
        Assert.Equal(-1L, ledgerA.MerkleSkipIndex.FindDivergenceIndex(ledgerB.MerkleSkipIndex));
        Assert.True(ledgerA.Verify());
        Assert.True(ledgerB.Verify());
    }

    private static IReadOnlyList<OperatorEvent> CreateCompletedRunEvents()
    {
        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Sync Tester", ReferenceNow.AddMinutes(-10));
        var loadout = new LoadoutChangedEvent(operatorId, 1, "Rifle", created.Hash, ReferenceNow.AddMinutes(-9));
        var perk = new PerkUnlockedEvent(operatorId, 2, "Scavenger", loadout.Hash, ReferenceNow.AddMinutes(-8));
        var infil = new InfilStartedEvent(
            operatorId,
            3,
            Guid.NewGuid(),
            "Rifle|Medkit",
            ReferenceNow.AddMinutes(-7),
            perk.Hash,
            ReferenceNow.AddMinutes(-7));
        var combatStart = new CombatSessionStartedEvent(operatorId, 4, Guid.NewGuid(), infil.Hash, ReferenceNow.AddMinutes(-6));
        var xp = new XpGainedEvent(operatorId, 5, 150, "MissionComplete", combatStart.Hash, ReferenceNow.AddMinutes(-5));
        var victory = new CombatVictoryEvent(operatorId, 6, xp.Hash, ReferenceNow.AddMinutes(-4));
        var exfil = new InfilEndedEvent(operatorId, 7, true, "EXFIL", victory.Hash, ReferenceNow.AddMinutes(-3));

        return [created, loadout, perk, infil, combatStart, xp, victory, exfil];
    }

    private static ServerIdentity CreateServerIdentity()
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);

        var serverPrivateKey = ServerIdentity.GeneratePrivateKey();
        var certificate = certificateIssuer.IssueServerCertificate(
            Guid.NewGuid(),
            ServerIdentity.GetPublicKey(serverPrivateKey),
            ReferenceNow.AddMinutes(-5),
            ReferenceNow.AddMinutes(30));

        return new ServerIdentity(certificate, serverPrivateKey);
    }
}
