using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class AuthoritySecurityTests
{
    [Fact]
    public void VerifyServerCertificate_ReturnsTrue_ForRootSignedUnexpiredCertificate()
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);
        var authorityRoot = new AuthorityRoot(certificateIssuer.RootPublicKey);
        var serverId = Guid.NewGuid();
        var serverPublicKey = ServerIdentity.GetPublicKey(ServerIdentity.GeneratePrivateKey());
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var validUntil = DateTimeOffset.UtcNow.AddMinutes(30);

        var certificate = certificateIssuer.IssueServerCertificate(serverId, serverPublicKey, issuedAt, validUntil);

        Assert.True(authorityRoot.VerifyServerCertificate(certificate));
    }

    [Fact]
    public void VerifyServerCertificate_ReturnsFalse_ForExpiredCertificate()
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);
        var authorityRoot = new AuthorityRoot(certificateIssuer.RootPublicKey);
        var serverId = Guid.NewGuid();
        var serverPublicKey = ServerIdentity.GetPublicKey(ServerIdentity.GeneratePrivateKey());

        var certificate = certificateIssuer.IssueServerCertificate(
            serverId,
            serverPublicKey,
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1));

        Assert.False(authorityRoot.VerifyServerCertificate(certificate));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsTrue_ForValidSignedValidation()
    {
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);

        var validation = serverIdentity.SignRunValidation(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3, 4]);

        Assert.True(verifier.VerifyRunSignature(validation, serverIdentity.Certificate));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsFalse_WhenFinalStateHashIsTampered()
    {
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
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
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
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

    [Fact]
    public void VerifySignedRunValidation_Succeeds_WhenCertificateAndSignatureValid()
    {
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);

        var signedValidation = serverIdentity.SignSignedRunValidation(Guid.NewGuid(), Guid.NewGuid(), [11, 22, 33, 44]);

        Assert.True(verifier.Verify(signedValidation));
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
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddMinutes(30));

        return new ServerIdentity(certificate, serverPrivateKey);
    }
}
