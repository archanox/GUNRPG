using System.Security.Cryptography;

namespace GUNRPG.Security;

public sealed class RunValidationResult
{
    private readonly byte[] _finalStateHash;

    public RunValidationResult(
        Guid runId,
        Guid playerId,
        Guid serverId,
        byte[] finalStateHash,
        SignedRunValidation attestation,
        GUNRPG.Ledger.RunLedgerMutation? mutation = null)
    {
        RunId = runId;
        PlayerId = playerId;
        ServerId = serverId;
        _finalStateHash = AuthorityCrypto.CloneAndValidateSha256Hash(finalStateHash);
        if (attestation == null) throw new ArgumentNullException(nameof(attestation));
        if (attestation.Validation == null)
            throw new ArgumentException("Attestation Validation must not be null.", nameof(attestation));
        if (attestation.Certificate == null)
            throw new ArgumentException("Attestation Certificate must not be null.", nameof(attestation));
        if (attestation.Validation.RunId != runId)
            throw new ArgumentException("Attestation RunId does not match RunId.", nameof(attestation));
        if (attestation.Validation.PlayerId != playerId)
            throw new ArgumentException("Attestation PlayerId does not match PlayerId.", nameof(attestation));
        if (attestation.Validation.ServerId != serverId)
            throw new ArgumentException("Attestation ServerId does not match ServerId.", nameof(attestation));
        if (attestation.Certificate.ServerId != serverId)
            throw new ArgumentException("Attestation Certificate ServerId does not match ServerId.", nameof(attestation));
        if (!CryptographicOperations.FixedTimeEquals(attestation.Validation.FinalStateHash, _finalStateHash))
            throw new ArgumentException("Attestation FinalStateHash does not match FinalStateHash.", nameof(attestation));
        Attestation = attestation;
        Mutation = mutation ?? GUNRPG.Ledger.RunLedgerMutation.Empty;
    }

    public Guid RunId { get; }

    public Guid PlayerId { get; }

    public Guid ServerId { get; }

    public byte[] FinalStateHash => (byte[])_finalStateHash.Clone();

    public SignedRunValidation Attestation { get; }

    public GUNRPG.Ledger.RunLedgerMutation Mutation { get; }

    public byte[] ComputeResultHash() => ComputeResultHash(this);

    public static byte[] ComputeResultHash(RunValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return ComputeResultHash(
            result.RunId,
            result.PlayerId,
            result.ServerId,
            result._finalStateHash);
    }

    internal static byte[] ComputeResultHash(
        Guid runId,
        Guid playerId,
        Guid serverId,
        byte[] finalStateHash) =>
        AuthorityCrypto.ComputeRunResultHash(runId, playerId, serverId, finalStateHash);

    internal static byte[] ComputeResultHash(RunValidationSignature validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return ComputeResultHash(
            validation.RunId,
            validation.PlayerId,
            validation.ServerId,
            validation.FinalStateHash);
    }
}
