using System.Security.Cryptography;

namespace GUNRPG.Security;

public sealed class ServerIdentity
{
    private readonly byte[] _serverPrivateKey;

    public ServerIdentity(ServerCertificate certificate, byte[] serverPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var normalizedPrivateKey = AuthorityCrypto.CloneAndValidatePrivateKey(serverPrivateKey);
        var derivedPublicKey = AuthorityCrypto.GetPublicKey(normalizedPrivateKey);
        var certificatePublicKey = certificate.PublicKey;
        if (!CryptographicOperations.FixedTimeEquals(derivedPublicKey, certificatePublicKey))
        {
            throw new ArgumentException("The supplied private key does not match the certificate public key.", nameof(serverPrivateKey));
        }

        Certificate = certificate;
        _serverPrivateKey = normalizedPrivateKey;
    }

    public ServerCertificate Certificate { get; }

    public RunValidationSignature SignRunValidation(
        Guid runId,
        Guid playerId,
        byte[] finalStateHash)
    {
        var payloadHash = AuthorityCrypto.ComputeRunValidationPayloadHash(runId, playerId, finalStateHash);
        var signature = AuthorityCrypto.SignHashedPayload(_serverPrivateKey, payloadHash);
        return new RunValidationSignature(runId, playerId, finalStateHash, Certificate.ServerId, signature);
    }

    public SignedRunValidation SignSignedRunValidation(Guid runId, Guid playerId, byte[] finalStateHash) =>
        new(SignRunValidation(runId, playerId, finalStateHash), Certificate);

    public static byte[] GeneratePrivateKey() => AuthorityCrypto.GeneratePrivateKey();

    public static byte[] GetPublicKey(byte[] privateKey) => AuthorityCrypto.GetPublicKey(privateKey);
}
