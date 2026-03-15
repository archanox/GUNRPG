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
        Assert.True(result.FinalStateHash.SequenceEqual(result.Attestation.Validation.FinalStateHash));
        Assert.Equal(runId, result.Attestation.Validation.RunId);
        Assert.Equal(playerId, result.Attestation.Validation.PlayerId);
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
