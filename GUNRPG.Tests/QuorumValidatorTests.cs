using GUNRPG.Core.Operators;
using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class QuorumValidatorTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 17, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void QuorumValidator_RejectsUntrustedSigner()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var trustedAuthority = CreateAuthority("trusted", out _);
        var untrustedAuthority = CreateAuthority("untrusted", out var untrustedPrivateKey);
        var validation = WithSignatures(
            result.Attestation,
            SignValidation(result, untrustedAuthority, untrustedPrivateKey));

        var hasQuorum = validator.HasQuorum(
            validation,
            new AuthoritySet([trustedAuthority]),
            new QuorumPolicy(1));

        Assert.False(hasQuorum);
    }

    [Fact]
    public void QuorumValidator_RejectsDuplicateSignatures()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var authority = CreateAuthority("authority-a", out var privateKey);
        var signature = SignValidation(result, authority, privateKey);
        var validation = WithSignatures(result.Attestation, signature, SignValidation(result, authority, privateKey));

        var hasQuorum = validator.HasQuorum(
            validation,
            new AuthoritySet([authority]),
            new QuorumPolicy(1));

        Assert.False(hasQuorum);
    }

    [Fact]
    public void QuorumValidator_RequiresMinimumSignatures()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out _);
        var validation = WithSignatures(
            result.Attestation,
            SignValidation(result, authorityA, privateKeyA));

        var hasQuorum = validator.HasQuorum(
            validation,
            new AuthoritySet([authorityA, authorityB]),
            new QuorumPolicy(2));

        Assert.False(hasQuorum);
    }

    [Fact]
    public void QuorumValidator_AcceptsValidQuorum()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var validation = WithSignatures(
            result.Attestation,
            SignValidation(result, authorityA, privateKeyA),
            SignValidation(result, authorityB, privateKeyB));

        var hasQuorum = validator.HasQuorum(
            validation,
            new AuthoritySet([authorityA, authorityB, authorityC]),
            new QuorumPolicy(2));

        Assert.True(hasQuorum);
    }

    [Fact]
    public void QuorumValidator_RejectsMismatchedResult()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var otherResult = CreateValidatedResult(seed: 8);
        var authority = CreateAuthority("authority-a", out var privateKey);
        var validation = WithSignatures(
            result.Attestation,
            SignValidation(otherResult, authority, privateKey));

        var hasQuorum = validator.HasQuorum(
            validation,
            new AuthoritySet([authority]),
            new QuorumPolicy(1));

        Assert.False(hasQuorum);
    }

    [Fact]
    public void LedgerTryAppendWithQuorum_AppendsQuorumApprovedRun()
    {
        var validator = new QuorumValidator();
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authoritySet = new AuthoritySet([authorityA, authorityB]);
        var policy = new QuorumPolicy(2);
        var serverIdentity = CreateServerIdentity();
        var engine = new RunReplayEngine();
        var result = engine.ValidateAndSignRun(Guid.NewGuid(), Guid.NewGuid(), CreateCompletedRunEvents(), serverIdentity);
        var attestation = WithSignatures(
            result.Attestation,
            SignValidation(result, authorityA, privateKeyA),
            SignValidation(result, authorityB, privateKeyB));
        var quorumApprovedResult = new RunValidationResult(
            result.RunId,
            result.PlayerId,
            result.ServerId,
            result.FinalStateHash,
            attestation);
        var ledger = new RunLedger();

        var appended = ledger.TryAppendWithQuorum(quorumApprovedResult, validator, authoritySet, policy);

        Assert.True(appended);
        Assert.Single(ledger.Entries);
    }

    [Fact]
    public void SignatureMerge_CombinesPartialQuorum()
    {
        var validator = new QuorumValidator();
        var result = CreateValidatedResult();
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var partialA = WithSignatures(result.Attestation, SignValidation(result, authorityA, privateKeyA));
        var partialB = WithSignatures(result.Attestation, SignValidation(result, authorityB, privateKeyB));

        var merged = SignedRunValidation.MergeSignatures(partialA, partialB);

        Assert.Equal(2, merged.Signatures.Count);
        Assert.True(validator.HasQuorum(merged, new AuthoritySet([authorityA, authorityB]), new QuorumPolicy(2)));
    }

    [Fact]
    public void ResultHash_IsDeterministic()
    {
        var result = CreateValidatedResult();
        var equivalentResult = new RunValidationResult(
            result.RunId,
            result.PlayerId,
            result.ServerId,
            result.FinalStateHash,
            new SignedRunValidation(result.Attestation.Validation, result.Attestation.Certificate));

        var hashA = RunValidationResult.ComputeResultHash(result);
        var hashB = RunValidationResult.ComputeResultHash(result);
        var hashC = RunValidationResult.ComputeResultHash(equivalentResult);

        Assert.True(hashA.SequenceEqual(hashB));
        Assert.True(hashA.SequenceEqual(hashC));
    }

    [Fact]
    public void LedgerEntryHash_ExcludesAuthoritySignatures()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var result = CreateValidatedResult();
        var timestamp = ReferenceNow.AddMinutes(1);
        var entryWithOneSignature = CreateLedgerEntry(
            AttachSignatures(result, SignValidation(result, authorityA, privateKeyA)),
            timestamp);
        var entryWithTwoSignatures = CreateLedgerEntry(
            AttachSignatures(
                result,
                SignValidation(result, authorityA, privateKeyA),
                SignValidation(result, authorityB, privateKeyB)),
            timestamp);

        Assert.True(entryWithOneSignature.EntryHash.SequenceEqual(entryWithTwoSignatures.EntryHash));
    }

    private static RunValidationResult CreateValidatedResult(byte seed = 1)
    {
        var serverIdentity = CreateServerIdentity();
        var signedValidation = serverIdentity.SignSignedRunValidation(Guid.NewGuid(), Guid.NewGuid(), CreateHash(seed));
        return new RunValidationResult(
            signedValidation.Validation.RunId,
            signedValidation.Validation.PlayerId,
            signedValidation.Validation.ServerId,
            signedValidation.Validation.FinalStateHash,
            signedValidation);
    }

    private static Authority CreateAuthority(string id, out byte[] privateKey)
    {
        privateKey = AuthorityCrypto.GeneratePrivateKey();
        return new Authority(AuthorityCrypto.GetPublicKey(privateKey), id);
    }

    private static AuthoritySignature SignValidation(
        RunValidationResult result,
        Authority authority,
        byte[] privateKey)
    {
        return new AuthoritySignature(
            authority.PublicKey,
            AuthorityCrypto.SignHashedPayload(privateKey, RunValidationResult.ComputeResultHash(result)));
    }

    private static SignedRunValidation WithSignatures(
        SignedRunValidation validation,
        params AuthoritySignature[] signatures)
    {
        return new SignedRunValidation(validation.Validation, validation.Certificate)
        {
            Signatures = [.. signatures]
        };
    }

    private static RunValidationResult AttachSignatures(
        RunValidationResult result,
        params AuthoritySignature[] signatures)
    {
        return new RunValidationResult(
            result.RunId,
            result.PlayerId,
            result.ServerId,
            result.FinalStateHash,
            WithSignatures(result.Attestation, signatures));
    }

    private static RunLedgerEntry CreateLedgerEntry(
        RunValidationResult result,
        DateTimeOffset timestamp)
    {
        var ledger = new RunLedger();
        return ledger.Append(result, timestamp);
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

    private static byte[] CreateHash(byte seed)
    {
        var value = new byte[32];
        for (var i = 0; i < value.Length; i++)
        {
            value[i] = (byte)(seed + i);
        }

        return value;
    }

    private static IReadOnlyList<OperatorEvent> CreateCompletedRunEvents()
    {
        var operatorId = OperatorId.NewId();
        var created = new OperatorCreatedEvent(operatorId, "Quorum Tester", ReferenceNow.AddMinutes(-10));
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
}
