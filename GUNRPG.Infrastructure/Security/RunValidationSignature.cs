namespace GUNRPG.Security;

public sealed class RunValidationSignature
{
    private readonly byte[] _finalStateHash;
    private readonly byte[] _signature;

    public RunValidationSignature(
        Guid runId,
        Guid playerId,
        byte[] finalStateHash,
        Guid serverId,
        byte[] signature)
    {
        RunId = runId;
        PlayerId = playerId;
        _finalStateHash = AuthorityCrypto.CloneAndValidateSha256Hash(finalStateHash);
        ServerId = serverId;
        _signature = AuthorityCrypto.CloneAndValidateSignature(signature);
    }

    public Guid RunId { get; }

    public Guid PlayerId { get; }

    public byte[] FinalStateHash => (byte[])_finalStateHash.Clone();

    public Guid ServerId { get; }

    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;

    internal byte[] ComputePayloadHash() =>
        AuthorityCrypto.ComputeRunValidationPayloadHash(RunId, PlayerId, _finalStateHash);
}
