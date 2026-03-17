using System.Security.Cryptography;

namespace GUNRPG.Security;

public sealed class SignedRunValidation
{
    private readonly byte[] _resultHash;

    public SignedRunValidation(
        RunValidationSignature validation,
        ServerCertificate certificate)
    {
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
        Certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
        _resultHash = RunValidationResult.ComputeResultHash(Validation);
    }

    public RunValidationSignature Validation { get; }

    public ServerCertificate Certificate { get; }

    public List<AuthoritySignature> Signatures { get; init; } = [];

    public byte[] ResultHash => (byte[])_resultHash.Clone();

    internal byte[] ComputeResultHash() => ResultHash;

    /// <summary>
    /// Merges authority signatures for the same attested run result.
    /// If attestation material does not match, the original instance <paramref name="a"/> is returned unchanged.
    /// </summary>
    public static SignedRunValidation Merge(
        SignedRunValidation a,
        SignedRunValidation b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var aResultHash = a._resultHash;
        var bResultHash = b._resultHash;
        if (!CryptographicOperations.FixedTimeEquals(aResultHash, bResultHash))
        {
            return a;
        }

        if (!HasMatchingAttestationMaterial(a, b))
        {
            return a;
        }

        var mergedSignatures = new List<AuthoritySignature>();
        var seenSigners = new HashSet<string>(StringComparer.Ordinal);

        AddValidUniqueSignatures(a.Signatures, aResultHash, seenSigners, mergedSignatures);
        AddValidUniqueSignatures(b.Signatures, aResultHash, seenSigners, mergedSignatures);
        var orderedItems = mergedSignatures
            .Select(static signature => (Signature: signature, SignerId: AuthoritySignatureOrdering.CreateSignerId(signature.PublicKeyBytes)))
            .ToList();
        orderedItems.Sort(static (left, right) =>
        {
            return AuthoritySignatureOrdering.Compare(
                left.SignerId,
                left.Signature.SignatureBytes,
                right.SignerId,
                right.Signature.SignatureBytes);
        });

        return new SignedRunValidation(a.Validation, a.Certificate)
        {
            Signatures = [.. orderedItems.Select(static item => item.Signature)]
        };
    }

    public static SignedRunValidation MergeSignatures(
        SignedRunValidation a,
        SignedRunValidation b) => Merge(a, b);

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

            var signerId = AuthoritySignatureOrdering.CreateSignerId(signature.PublicKeyBytes);
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
            && CryptographicOperations.FixedTimeEquals(a.Certificate.PublicKeyBytes, b.Certificate.PublicKeyBytes);
    }
}
