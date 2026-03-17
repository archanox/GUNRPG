namespace GUNRPG.Security;

public sealed class SignedRunValidation
{
    public SignedRunValidation(
        RunValidationSignature validation,
        ServerCertificate certificate)
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
    }

    public RunValidationSignature Validation { get; }

    public ServerCertificate Certificate { get; }

    public List<AuthoritySignature> Signatures { get; init; } = [];

    internal byte[] ComputeResultHash() =>
        AuthorityCrypto.ComputeRunResultHash(
            Validation.RunId,
            Validation.PlayerId,
            Validation.ServerId,
            Validation.FinalStateHash);
}
