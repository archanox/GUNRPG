namespace GUNRPG.Security;

public sealed class SignatureVerifier
{
    private readonly AuthorityRoot _authorityRoot;

    public SignatureVerifier(AuthorityRoot authorityRoot)
    {
        _authorityRoot = authorityRoot ?? throw new ArgumentNullException(nameof(authorityRoot));
    }

    public bool Verify(SignedRunValidation record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        return VerifyRunSignature(record.Validation, record.Certificate, now);
    }

    public bool VerifyRunSignature(
        RunValidationSignature validation,
        ServerCertificate cert,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(cert);

        if (!_authorityRoot.VerifyServerCertificate(cert, now))
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
