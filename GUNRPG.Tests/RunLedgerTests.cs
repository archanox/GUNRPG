using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
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
        Assert.Equal(new byte[32], entry.PreviousHash);
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

        var entry1 = ledger.Append(result1);
        ledger.Append(result2);

        // Tamper with the first entry's hash
        entry1.EntryHash[0] ^= 0xFF;

        Assert.False(ledger.Verify());
    }

    [Fact]
    public void LedgerVerify_ReturnsTrueForEmptyLedger()
    {
        var ledger = new RunLedger();
        Assert.True(ledger.Verify());
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
}
