namespace GUNRPG.Security;

/// <summary>
/// Represents this node's identity within the authority network.
/// Determines whether the node is an <see cref="AuthorityRole.Authority"/> or a
/// <see cref="AuthorityRole.Verifier"/> based on whether its private key's public key
/// exists in the <see cref="AuthorityRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Authority nodes possess a private key stored at <c>keys/authority.key</c> whose
/// corresponding public key is listed in <c>config/authorities.json</c>.
/// </para>
/// <para>
/// Nodes without a key file, or whose key is not in the registry, are verifier nodes.
/// </para>
/// </remarks>
public sealed class NodeIdentity
{
    private readonly byte[]? _publicKey;

    private NodeIdentity(AuthorityRole role, byte[]? publicKey)
    {
        Role = role;
        _publicKey = publicKey is not null ? (byte[])publicKey.Clone() : null;
    }

    /// <summary>The operational role this node performs.</summary>
    public AuthorityRole Role { get; }

    /// <summary>
    /// The Ed25519 public key for this node (32 bytes), or <see langword="null"/> if the node
    /// has no private key.
    /// </summary>
    public byte[]? PublicKey => _publicKey is not null ? (byte[])_publicKey.Clone() : null;

    /// <summary>
    /// Returns <see langword="true"/> if this node has the <see cref="AuthorityRole.Authority"/> role.
    /// </summary>
    public bool IsAuthority => Role == AuthorityRole.Authority;

    /// <summary>
    /// Returns a short fingerprint of this node's public key for use in logging and diagnostics.
    /// The fingerprint is the first 16 hex characters (8 bytes) of the public key, prefixed with
    /// <c>ed25519:</c>.
    /// Returns <see langword="null"/> when the node has no private key (i.e. when
    /// <see cref="PublicKey"/> is <see langword="null"/>).
    /// </summary>
    /// <example>
    /// <code>
    /// Console.WriteLine($"Authority node detected ({identity.GetFingerprint()})");
    /// // e.g. "Authority node detected (ed25519:abcd1234ef567890)"
    /// </code>
    /// </example>
    public string? GetFingerprint()
    {
        if (_publicKey is null)
            return null;

        return $"ed25519:{Convert.ToHexString(_publicKey[..8]).ToLowerInvariant()}";
    }

    /// <summary>
    /// Creates an anonymous (verifier-only) node identity with no private key.
    /// </summary>
    public static NodeIdentity Anonymous() => new(AuthorityRole.Verifier, null);

    /// <summary>
    /// Loads a node identity from a private key file and checks the key against the
    /// authority registry to determine the node's role.
    /// </summary>
    /// <param name="privateKeyPath">
    /// Path to the raw 32-byte Ed25519 private key file (e.g. <c>keys/authority.key</c>).
    /// </param>
    /// <param name="registry">
    /// The authority registry used to determine whether this node is trusted.
    /// </param>
    /// <returns>
    /// A <see cref="NodeIdentity"/> with <see cref="AuthorityRole.Authority"/> when the public key
    /// derived from <paramref name="privateKeyPath"/> is found in <paramref name="registry"/>,
    /// or <see cref="AuthorityRole.Verifier"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="privateKeyPath"/> or <paramref name="registry"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the key file does not contain exactly 32 bytes.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the file at <paramref name="privateKeyPath"/> does not exist.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the file at <paramref name="privateKeyPath"/> exists but cannot be read due to
    /// insufficient permissions.
    /// </exception>
    public static NodeIdentity Load(string privateKeyPath, AuthorityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        ArgumentNullException.ThrowIfNull(registry);

        byte[] keyBytes;
        try
        {
            keyBytes = File.ReadAllBytes(privateKeyPath);
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(
                $"Authority private key file not found: {privateKeyPath}", privateKeyPath);
        }
        if (keyBytes.Length != AuthorityCrypto.KeySize)
            throw new ArgumentException(
                $"Authority private key must be exactly {AuthorityCrypto.KeySize} bytes, " +
                $"got {keyBytes.Length} bytes at '{privateKeyPath}'.",
                nameof(privateKeyPath));

        var publicKey = AuthorityCrypto.GetPublicKey(keyBytes);
        var role = registry.IsTrustedAuthority(publicKey)
            ? AuthorityRole.Authority
            : AuthorityRole.Verifier;

        return new NodeIdentity(role, publicKey);
    }

    /// <summary>
    /// Attempts to load a node identity, returning an anonymous verifier if the key file
    /// is absent.  Any error reading or validating the key is propagated to the caller.
    /// </summary>
    /// <param name="privateKeyPath">
    /// Path to the raw 32-byte Ed25519 private key file.
    /// </param>
    /// <param name="registry">
    /// The authority registry used to determine whether this node is trusted.
    /// </param>
    /// <returns>
    /// A <see cref="NodeIdentity"/> with <see cref="AuthorityRole.Authority"/> when the key file exists
    /// and its public key is in the registry; <see cref="AuthorityRole.Verifier"/> when the file is absent;
    /// and propagates any exception when the file exists but cannot be read or is invalid.
    /// </returns>
    public static NodeIdentity TryLoad(string privateKeyPath, AuthorityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        ArgumentNullException.ThrowIfNull(registry);

        try
        {
            return Load(privateKeyPath, registry);
        }
        catch (FileNotFoundException)
        {
            return Anonymous();
        }
    }
}
