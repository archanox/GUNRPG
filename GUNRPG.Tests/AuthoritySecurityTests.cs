using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class AuthoritySecurityTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 15, 04, 00, 00, TimeSpan.Zero);

    [Fact]
    public void VerifyServerCertificate_ReturnsTrue_ForRootSignedUnexpiredCertificate()
    {
        var rootPrivateKey = CertificateIssuer.GeneratePrivateKey();
        var certificateIssuer = new CertificateIssuer(rootPrivateKey);
        var authorityRoot = new AuthorityRoot(certificateIssuer.RootPublicKey);
        var serverId = Guid.NewGuid();
        var serverPublicKey = ServerIdentity.GetPublicKey(ServerIdentity.GeneratePrivateKey());
        var issuedAt = ReferenceNow.AddMinutes(-5);
        var validUntil = ReferenceNow.AddMinutes(30);

        var certificate = certificateIssuer.IssueServerCertificate(serverId, serverPublicKey, issuedAt, validUntil);

        Assert.True(authorityRoot.VerifyServerCertificate(certificate, ReferenceNow));
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
            ReferenceNow.AddHours(-2),
            ReferenceNow.AddHours(-1));

        Assert.False(authorityRoot.VerifyServerCertificate(certificate, ReferenceNow));
    }

    [Fact]
    public void VerifyRunSignature_ReturnsTrue_ForValidSignedValidation()
    {
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);

        var validation = serverIdentity.SignRunValidation(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3, 4]);

        Assert.True(verifier.VerifyRunSignature(validation, serverIdentity.Certificate, ReferenceNow));
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

        Assert.False(verifier.VerifyRunSignature(tampered, serverIdentity.Certificate, ReferenceNow));
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

        Assert.False(verifier.VerifyRunSignature(mismatched, serverIdentity.Certificate, ReferenceNow));
    }

    [Fact]
    public void VerifySignedRunValidation_Succeeds_WhenCertificateAndSignatureValid()
    {
        var serverIdentity = CreateServerIdentity(out var authorityRoot);
        var verifier = new SignatureVerifier(authorityRoot);

        var signedValidation = serverIdentity.SignSignedRunValidation(Guid.NewGuid(), Guid.NewGuid(), [11, 22, 33, 44]);

        Assert.True(verifier.Verify(signedValidation, ReferenceNow));
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
