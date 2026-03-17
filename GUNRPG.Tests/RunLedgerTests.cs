using System.Collections.Immutable;
using System.Security.Cryptography;
using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
using GUNRPG.Ledger.Indexing;
using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class RunLedgerTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void LedgerAppend_CreatesValidHashChain()
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

        Assert.True(ledger.Verify());
    }

    [Fact]
    public void LedgerAppend_FirstEntryUsesZeroPreviousHash()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var entry = ledger.Append(result);

        Assert.Equal(0L, entry.Index);
        Assert.True(entry.PreviousHash.SequenceEqual(new byte[32]));
    }

    [Fact]
    public void LedgerAppend_ChainsPreviousHashCorrectly()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        var entry1 = ledger.Append(result1);
        var entry2 = ledger.Append(result2);

        Assert.Equal(0L, entry1.Index);
        Assert.Equal(1L, entry2.Index);
        Assert.True(entry1.EntryHash.SequenceEqual(entry2.PreviousHash));
    }

    [Fact]
    public void LedgerDetectsTampering()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledger.Append(result1);
        ledger.Append(result2);

        // Tamper: replace the first entry with a corrupted EntryHash
        var corruptedHash = ImmutableArray.Create(new byte[SHA256.HashSizeInBytes]); // all-zero, not the real hash
        ledger.ReplaceEntryForTest(0, ledger.Entries[0] with { EntryHash = corruptedHash });

        Assert.False(ledger.Verify());
    }

    [Fact]
    public void LedgerVerify_ReturnsTrueForEmptyLedger()
    {
        var ledger = new RunLedger();
        Assert.True(ledger.Verify());
    }

    [Fact]
    public void LedgerHead_IsNullForEmptyLedger()
    {
        var ledger = new RunLedger();
        Assert.Null(ledger.Head);
    }

    [Fact]
    public void LedgerHead_ReturnsLastEntry()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        var result1 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var result2 = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);

        ledger.Append(result1);
        var entry2 = ledger.Append(result2);

        Assert.Equal(entry2, ledger.Head);
    }

    [Fact]
    public void LedgerAppend_IsDeterministic()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();

        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var fixedTimestamp = new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

        var ledger1 = new RunLedger();
        var entry1 = ledger1.Append(result, fixedTimestamp);

        var ledger2 = new RunLedger();
        var entry2 = ledger2.Append(result, fixedTimestamp);

        Assert.True(entry1.EntryHash.SequenceEqual(entry2.EntryHash));
    }

    [Fact]
    public void LedgerEntries_IsReadOnly()
    {
        var ledger = new RunLedger();
        // Entries must not be castable back to a mutable list
        Assert.IsNotType<List<RunLedgerEntry>>(ledger.Entries);
    }

    [Fact]
    public void MerkleIndex_UpdatesOnAppend()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledger = new RunLedger();

        for (var i = 0; i < 9; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            ledger.Append(result, ReferenceNow.AddMinutes(i));
        }

        Assert.False(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(0));
        Assert.True(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(1));
        Assert.True(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(2));
        Assert.False(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(3));
        Assert.True(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(4));
        Assert.True(ledger.MerkleSkipIndex.Checkpoints.ContainsKey(8));
    }

    [Fact]
    public void MerkleIndex_DetectsDivergence()
    {
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var ledgerA = new RunLedger();
        var ledgerB = new RunLedger();

        for (var i = 0; i < 16; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            var timestamp = ReferenceNow.AddMinutes(i);

            var entryA = ledgerA.Append(result, timestamp);
            var entryB = ledgerB.Append(result, timestamp);

            Assert.True(entryA.EntryHash.SequenceEqual(entryB.EntryHash));
        }

        AppendRuns(ledgerA, engine, serverIdentity, 4, ReferenceNow.AddMinutes(16));
        AppendRuns(ledgerB, engine, serverIdentity, 6, ReferenceNow.AddHours(1));

        Assert.Equal(16L, ledgerA.MerkleSkipIndex.FindDivergenceIndex(ledgerB.MerkleSkipIndex));
    }

    private static IReadOnlyList<OperatorEvent> CreateCompletedRunEvents()
    {
        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Ledger Tester", ReferenceNow.AddMinutes(-10));
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

    private static void AppendRuns(
        RunLedger ledger,
        RunReplayEngine engine,
        ServerIdentity serverIdentity,
        int count,
        DateTimeOffset startTime)
    {
        for (var i = 0; i < count; i++)
        {
            var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
            ledger.Append(result, startTime.AddMinutes(i));
        }
    }
}
