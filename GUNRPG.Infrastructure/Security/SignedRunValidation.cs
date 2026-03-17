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

    internal byte[] ComputeResultHash() => RunValidationResult.ComputeResultHash(Validation);

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

        if (!HasMatchingAttestationMaterial(a, b))
        {
            throw new ArgumentException("Signed validations must have identical attestation material to merge signatures.", nameof(b));
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
        var signatureIndex = 0;
        foreach (var signature in signatures)
        {
            if (signature is null)
            {
                throw new ArgumentException($"Signature collections must not contain null entries (index {signatureIndex}).", nameof(signatures));
            }

            signatureIndex++;

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

    private static bool HasMatchingAttestationMaterial(
        SignedRunValidation a,
        SignedRunValidation b)
    {
        return CryptographicOperations.FixedTimeEquals(a.Validation.SignatureBytes, b.Validation.SignatureBytes)
            && a.Certificate.ServerId == b.Certificate.ServerId
            && a.Certificate.IssuedAt == b.Certificate.IssuedAt
            && a.Certificate.ValidUntil == b.Certificate.ValidUntil
            && CryptographicOperations.FixedTimeEquals(a.Certificate.PublicKey, b.Certificate.PublicKey)
            && CryptographicOperations.FixedTimeEquals(a.Certificate.Signature, b.Certificate.Signature);
    }
}
