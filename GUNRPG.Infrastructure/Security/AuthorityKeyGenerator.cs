namespace GUNRPG.Security;

/// <summary>
/// Provides utilities for generating Ed25519 key pairs suitable for use as authority keys.
/// </summary>
/// <remarks>
/// <para>
/// Usage:
/// <code>
/// gunrpg keygen authority
/// </code>
/// Outputs two files:
/// <list type="bullet">
///   <item><c>authority_private.key</c> — raw 32-byte Ed25519 private key.</item>
///   <item><c>authority_public.key</c> — raw 32-byte Ed25519 public key.</item>
/// </list>
/// The public key can then be added to <c>config/authorities.json</c> in the
/// <c>ed25519:&lt;hex&gt;</c> format printed by <see cref="FormatPublicKeyEntry"/>.
/// </para>
/// </remarks>
public static class AuthorityKeyGenerator
{
    /// <summary>
    /// Generates a new random Ed25519 key pair.
    /// </summary>
    /// <returns>
    /// A tuple of (privateKey, publicKey), each exactly 32 bytes.
    /// </returns>
    public static (byte[] privateKey, byte[] publicKey) GenerateKeyPair()
    {
        var privateKey = AuthorityCrypto.GeneratePrivateKey();
        var publicKey = AuthorityCrypto.GetPublicKey(privateKey);
        return (privateKey, publicKey);
    }

    /// <summary>
    /// Writes the private and public keys to separate files.
    /// </summary>
    /// <param name="privateKeyPath">Destination path for the 32-byte private key file.</param>
    /// <param name="publicKeyPath">Destination path for the 32-byte public key file.</param>
    /// <param name="privateKey">The private key bytes (32 bytes).</param>
    /// <param name="publicKey">The public key bytes (32 bytes).</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when a key is not exactly 32 bytes.</exception>
    public static void WriteKeyFiles(
        string privateKeyPath,
        string publicKeyPath,
        byte[] privateKey,
        byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        ArgumentNullException.ThrowIfNull(publicKeyPath);
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (privateKey.Length != AuthorityCrypto.KeySize)
            throw new ArgumentException(
                $"Private key must be exactly {AuthorityCrypto.KeySize} bytes.", nameof(privateKey));
        if (publicKey.Length != AuthorityCrypto.KeySize)
            throw new ArgumentException(
                $"Public key must be exactly {AuthorityCrypto.KeySize} bytes.", nameof(publicKey));

        File.WriteAllBytes(privateKeyPath, privateKey);
        File.WriteAllBytes(publicKeyPath, publicKey);

        // Restrict the private key to owner-read/write only on Unix platforms.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(
                privateKeyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Formats a public key as an <c>ed25519:&lt;hex&gt;</c> entry suitable for
    /// pasting into <c>config/authorities.json</c>.
    /// </summary>
    /// <param name="publicKey">The Ed25519 public key (32 bytes).</param>
    /// <returns>A string of the form <c>ed25519:&lt;lowercase-hex&gt;</c>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="publicKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="publicKey"/> is not exactly 32 bytes.
    /// </exception>
    public static string FormatPublicKeyEntry(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != AuthorityCrypto.KeySize)
            throw new ArgumentException(
                $"Public key must be exactly {AuthorityCrypto.KeySize} bytes.", nameof(publicKey));

        return $"ed25519:{Convert.ToHexString(publicKey).ToLowerInvariant()}";
    }
}
