using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class RunReplayEngineTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void ReplayRun_ProducesSignedValidation()
    {
        var runId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);
        var engine = new RunReplayEngine();

        var result = engine.ValidateAndSignRun(runId, playerId, CreateCompletedRunEvents(), serverIdentity);

        Assert.Equal(runId, result.RunId);
        Assert.Equal(playerId, result.PlayerId);
        Assert.Equal(serverIdentity.Certificate.ServerId, result.ServerId);
        Assert.True(result.FinalStateHash.SequenceEqual(result.Attestation.Validation.FinalStateHash));
        Assert.Equal(runId, result.Attestation.Validation.RunId);
        Assert.Equal(playerId, result.Attestation.Validation.PlayerId);
        Assert.Equal(result.ServerId, result.Attestation.Validation.ServerId);
        Assert.True(verifier.Verify(result.Attestation, ReferenceNow));
    }

    [Fact]
    public void ReplayRun_VerificationFails_WhenSignatureTampered()
    {
        var runId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);
        var engine = new RunReplayEngine();

        var result = engine.ValidateAndSignRun(runId, playerId, CreateCompletedRunEvents(), serverIdentity);
        var tamperedSignature = result.Attestation.Validation.Signature;
        tamperedSignature[0] ^= 0xFF;

        var tamperedAttestation = new SignedRunValidation(
            new RunValidationSignature(
                result.RunId,
                result.PlayerId,
                result.FinalStateHash,
                result.Attestation.Validation.ServerId,
                tamperedSignature),
            result.Attestation.Certificate);

        Assert.False(verifier.Verify(tamperedAttestation, ReferenceNow));
    }

    [Fact]
    public void ReplayRun_HashIsDeterministic()
    {
        var engine = new RunReplayEngine();
        var events = CreateCompletedRunEvents();

        var hash1 = engine.ValidateRunOnly(events);
        var hash2 = engine.ValidateRunOnly(events);

        Assert.True(hash1.SequenceEqual(hash2));
    }

    [Fact]
    public void Replay_IsDeterministic()
    {
        var serverIdentity = CreateServerIdentity(out _);
        var input = CreateRunInput();
        var engineA = new RunReplayEngine(serverIdentity);
        var engineB = new RunReplayEngine(serverIdentity);

        var resultA = engineA.Replay(input);
        var resultB = engineB.Replay(input);

        Assert.True(resultA.FinalStateHash.SequenceEqual(resultB.FinalStateHash));
        Assert.True(resultA.ComputeResultHash().SequenceEqual(resultB.ComputeResultHash()));
        Assert.True(resultA.Attestation.Validation.Signature.SequenceEqual(resultB.Attestation.Validation.Signature));
    }

    [Fact]
    public void Replay_ThrowsWhenMutationIsNull()
    {
        var serverIdentity = CreateServerIdentity(out _);
        var input = CreateRunInput() with { Mutation = null! };
        var engine = new RunReplayEngine(serverIdentity);

        Assert.Throws<ArgumentNullException>(() => engine.Replay(input));
    }

    [Fact]
    public void ValidateRunOnly_ThrowsWhenChainTamperedAtTail()
    {
        var engine = new RunReplayEngine();
        var events = CreateCompletedRunEvents().ToList();

        // Replace the last event with one that has a broken previous-hash so replay stops short
        var last = (InfilEndedEvent)events[^1];
        var (wasSuccessful, endReason) = last.GetPayload();
        var tampered = new InfilEndedEvent(
            last.OperatorId,
            last.SequenceNumber,
            wasSuccessful,
            endReason,
            new string('0', last.PreviousHash.Length), // all-zero previous hash breaks chain
            last.Timestamp);
        events[^1] = tampered;

        Assert.Throws<InvalidOperationException>(() => engine.ValidateRunOnly(events));
    }

    private static IReadOnlyList<OperatorEvent> CreateCompletedRunEvents()
    {
        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Replay Tester", ReferenceNow.AddMinutes(-10));
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

    private static RunInput CreateRunInput()
    {
        return new RunInput
        {
            RunId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Actions =
            [
                new PlayerAction { SequenceNumber = 1, Primary = PrimaryAction.Fire, Movement = MovementAction.WalkToward },
                new PlayerAction { SequenceNumber = 2, Stance = StanceAction.EnterADS },
                new PlayerAction { SequenceNumber = 3, Primary = PrimaryAction.Reload, CancelMovement = true }
            ]
        };
    }

    private static ServerIdentity CreateServerIdentity(out AuthorityRoot authorityRoot)
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);
        authorityRoot = new AuthorityRoot(certificateIssuer.RootPublicKey);

        var serverPrivateKey = ServerIdentity.GeneratePrivateKey();
        var certificate = certificateIssuer.IssueServerCertificate(
            Guid.NewGuid(),
            ServerIdentity.GetPublicKey(serverPrivateKey),
            ReferenceNow.AddMinutes(-5),
            ReferenceNow.AddMinutes(30));

        return new ServerIdentity(certificate, serverPrivateKey);
    }
}
