using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

public sealed class QuorumValidator
{
    /// <summary>
    /// Replays the given input and returns the resulting deterministic hash.
    /// Use this to compare hashes across nodes for quorum validation.
    /// </summary>
    public static byte[] ReplayHash(RunInput input, ReplayRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        runner ??= new ReplayRunner();
        var inputLog = InputLog.FromRunInput(input);
        var result = runner.Replay(inputLog);
        return (byte[])result.FinalHash.Clone();
    }

    public bool HasQuorum(
        SignedRunValidation validation,
        BootstrapAuthoritySet authorities,
        QuorumPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(authorities);
        return HasQuorum(validation, new AuthorityState(authorities.KeyIdentifiers), policy);
    }

    public bool HasQuorum(
        SignedRunValidation validation,
        AuthorityState authorities,
        QuorumPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(policy);

        return HasQuorum(validation.Signatures, validation.ComputeResultHash(), authorities, policy, excludedSignerPublicKey: null);
    }

    internal bool HasQuorum(
        IEnumerable<AuthoritySignature> signatures,
        byte[] payloadHash,
        AuthorityState authorities,
        QuorumPolicy policy,
        byte[]? excludedSignerPublicKey)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        ArgumentNullException.ThrowIfNull(payloadHash);
        ArgumentNullException.ThrowIfNull(authorities);
        ArgumentNullException.ThrowIfNull(policy);

        var excludedSignerId = excludedSignerPublicKey is null
            ? null
            : BootstrapAuthoritySet.CreateKeyIdentifier(excludedSignerPublicKey);
        var seenSigners = new HashSet<string>(StringComparer.Ordinal);
        var validSignatures = 0;

        foreach (var signature in signatures)
        {
            if (signature is null)
            {
                return false;
            }

            var signerId = BootstrapAuthoritySet.CreateKeyIdentifier(signature.PublicKeyBytes);
            if (!seenSigners.Add(signerId))
            {
                return false;
            }

            if (excludedSignerId is not null && signerId == excludedSignerId)
            {
                continue;
            }

            if (!authorities.IsTrusted(signature.PublicKeyBytes))
            {
                return false;
            }

            if (!AuthorityCrypto.VerifyHashedPayload(
                    signature.PublicKeyBytes,
                    payloadHash,
                    signature.SignatureBytes))
            {
                return false;
            }

            validSignatures++;
        }

        return validSignatures >= policy.RequiredSignatures;
    }
}
