namespace GUNRPG.Security;

public sealed class QuorumValidator
{
    public bool HasQuorum(
        SignedRunValidation validation,
        AuthoritySet authorities,
        QuorumPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(policy);

        var resultHash = validation.ComputeResultHash();
        var seenSigners = new HashSet<string>(StringComparer.Ordinal);
        var validSignatures = 0;

        foreach (var signature in validation.Signatures)
        {
            if (signature is null)
            {
                return false;
            }

            if (!authorities.IsTrusted(signature.PublicKeyBytes))
            {
                return false;
            }

            var signerId = AuthoritySet.CreateKeyIdentifier(signature.PublicKeyBytes);
            if (!seenSigners.Add(signerId))
            {
                return false;
            }

            if (!AuthorityCrypto.VerifyHashedPayload(
                    signature.PublicKeyBytes,
                    resultHash,
                    signature.SignatureBytes))
            {
                return false;
            }

            validSignatures++;
        }

        return validSignatures >= policy.RequiredSignatures;
    }
}
