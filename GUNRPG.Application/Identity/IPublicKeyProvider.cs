namespace GUNRPG.Application.Identity;

/// <summary>
/// Exposes the node's Ed25519 public key for external consumption.
/// Implemented by <c>JwtTokenService</c> in the Infrastructure layer.
/// Used by <c>GET /auth/token/public-key</c> to support future node-to-node trust exchange.
/// </summary>
public interface IPublicKeyProvider
{
    /// <summary>Returns the raw Ed25519 public key bytes (32 bytes).</summary>
    byte[] GetPublicKeyBytes();

    /// <summary>
    /// Returns the JWT <c>kid</c> (key ID) — a SHA-256 thumbprint of the public key.
    /// Validators use this to select the correct key when multiple key versions exist.
    /// </summary>
    string GetKeyId();
}
