namespace GUNRPG.Security;

public sealed class SignatureVerifier
{
    private readonly AuthorityRoot _authorityRoot;

    public SignatureVerifier(AuthorityRoot authorityRoot)
    {
        _authorityRoot = authorityRoot ?? throw new ArgumentNullException(nameof(authorityRoot));
    }

    public bool VerifyRunSignature(
        RunValidationSignature validation,
        ServerCertificate cert)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(cert);

        if (!_authorityRoot.VerifyServerCertificate(cert))
        {
            return false;
        }

        if (validation.ServerId != cert.ServerId)
        {
            return false;
        }

        return AuthorityCrypto.VerifyHashedPayload(
            cert.PublicKey,
            validation.ComputePayloadHash(),
            validation.Signature);
    }
}
