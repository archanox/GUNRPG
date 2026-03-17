using System.Security.Cryptography;

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
        RunValidationResult.ComputeResultHash(
            Validation.RunId,
            Validation.PlayerId,
            Validation.ServerId,
            Validation.FinalStateHash);

    public static SignedRunValidation MergeSignatures(
        SignedRunValidation a,
        SignedRunValidation b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var aResultHash = a.ComputeResultHash();
        var bResultHash = b.ComputeResultHash();
        if (!CryptographicOperations.FixedTimeEquals(aResultHash, bResultHash))
        {
            throw new ArgumentException("Signed validations must represent the same result to merge signatures.", nameof(b));
        }

        var mergedSignatures = new List<AuthoritySignature>();
        var seenSigners = new HashSet<string>(StringComparer.Ordinal);

        AddValidUniqueSignatures(a.Signatures, aResultHash, seenSigners, mergedSignatures);
        AddValidUniqueSignatures(b.Signatures, aResultHash, seenSigners, mergedSignatures);

        return new SignedRunValidation(a.Validation, a.Certificate)
        {
            Signatures = mergedSignatures
        };
    }

    private static void AddValidUniqueSignatures(
        IEnumerable<AuthoritySignature> signatures,
        byte[] resultHash,
        HashSet<string> seenSigners,
        List<AuthoritySignature> mergedSignatures)
    {
        foreach (var signature in signatures)
        {
            if (signature is null)
            {
                throw new ArgumentException("Signature collections must not contain null entries.", nameof(signatures));
            }

            var signerId = AuthoritySet.CreateKeyIdentifier(signature.PublicKeyBytes);
            if (!seenSigners.Add(signerId))
            {
                continue;
            }

            if (!AuthorityCrypto.VerifyHashedPayload(
                    signature.PublicKeyBytes,
                    resultHash,
                    signature.SignatureBytes))
            {
                continue;
            }

            mergedSignatures.Add(signature);
        }
    }
}
