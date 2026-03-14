using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class AuthoritySecurityTests
{
    [Fact]
    public void VerifyServerCertificate_ReturnsTrue_ForRootSignedUnexpiredCertificate()
    {
        var rootPrivateKey = AuthorityRoot.GeneratePrivateKey();
        var authorityRoot = new AuthorityRoot(AuthorityRoot.GetPublicKey(rootPrivateKey));
        var serverId = Guid.NewGuid();
        var serverPublicKey = ServerIdentity.GetPublicKey(ServerIdentity.GeneratePrivateKey());
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var validUntil = DateTimeOffset.UtcNow.AddMinutes(30);

        var certificate = authorityRoot.IssueServerCertificate(serverId, serverPublicKey, issuedAt, validUntil, rootPrivateKey);

        Assert.True(authorityRoot.VerifyServerCertificate(certificate));
    }

    [Fact]
    public void VerifyServerCertificate_ReturnsFalse_ForExpiredCertificate()
    {
        var rootPrivateKey = AuthorityRoot.GeneratePrivateKey();
        var authorityRoot = new AuthorityRoot(AuthorityRoot.GetPublicKey(rootPrivateKey));
        var serverId = Guid.NewGuid();
        var serverPublicKey = ServerIdentity.GetPublicKey(ServerIdentity.GeneratePrivateKey());

        var certificate = authorityRoot.IssueServerCertificate(
            serverId,
            serverPublicKey,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1),
            rootPrivateKey);

        Assert.False(authorityRoot.VerifyServerCertificate(certificate));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsTrue_ForValidSignedValidation()
    {
        var rootPrivateKey = AuthorityRoot.GeneratePrivateKey();
        var authorityRoot = new AuthorityRoot(AuthorityRoot.GetPublicKey(rootPrivateKey));
        var serverIdentity = ServerIdentity.Create(
            Guid.NewGuid(),
            rootPrivateKey,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));
        var verifier = new SignatureVerifier(authorityRoot);

        var validation = serverIdentity.SignRunValidation(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3, 4]);

        Assert.True(verifier.VerifyRunSignature(validation, serverIdentity.Certificate));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsFalse_WhenFinalStateHashIsTampered()
    {
        var rootPrivateKey = AuthorityRoot.GeneratePrivateKey();
        var authorityRoot = new AuthorityRoot(AuthorityRoot.GetPublicKey(rootPrivateKey));
        var serverIdentity = ServerIdentity.Create(
            Guid.NewGuid(),
            rootPrivateKey,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));
        var verifier = new SignatureVerifier(authorityRoot);
        var validation = serverIdentity.SignRunValidation(Guid.NewGuid(), Guid.NewGuid(), [10, 20, 30, 40]);
        var tampered = new RunValidationSignature(
            validation.RunId,
            validation.PlayerId,
            [10, 20, 30, 41],
            validation.ServerId,
            validation.Signature);

        Assert.False(verifier.VerifyRunSignature(tampered, serverIdentity.Certificate));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsFalse_WhenServerIdDoesNotMatchCertificate()
    {
        var rootPrivateKey = AuthorityRoot.GeneratePrivateKey();
        var authorityRoot = new AuthorityRoot(AuthorityRoot.GetPublicKey(rootPrivateKey));
        var serverIdentity = ServerIdentity.Create(
            Guid.NewGuid(),
            rootPrivateKey,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));
        var verifier = new SignatureVerifier(authorityRoot);
        var validation = serverIdentity.SignRunValidation(Guid.NewGuid(), Guid.NewGuid(), [7, 8, 9]);
        var mismatched = new RunValidationSignature(
            validation.RunId,
            validation.PlayerId,
            validation.FinalStateHash,
            Guid.NewGuid(),
            validation.Signature);

        Assert.False(verifier.VerifyRunSignature(mismatched, serverIdentity.Certificate));
    }
}
