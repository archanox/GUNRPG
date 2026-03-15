namespace GUNRPG.Security;

public sealed record RunValidationResult
{
    private readonly byte[] _finalStateHash;

    public RunValidationResult(
        Guid runId,
        Guid playerId,
        byte[] finalStateHash,
        SignedRunValidation attestation)
    {
        RunId = runId;
        PlayerId = playerId;
        _finalStateHash = AuthorityCrypto.CloneAndValidateSha256Hash(finalStateHash);
        Attestation = attestation ?? throw new ArgumentNullException(nameof(attestation));
    }

    public Guid RunId { get; }

    public Guid PlayerId { get; }

    public byte[] FinalStateHash => (byte[])_finalStateHash.Clone();

    public SignedRunValidation Attestation { get; }
}
