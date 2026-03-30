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
    public static NodeIdentity Load(string privateKeyPath, AuthorityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        ArgumentNullException.ThrowIfNull(registry);

        if (!File.Exists(privateKeyPath))
            throw new FileNotFoundException(
                $"Authority private key file not found: {privateKeyPath}", privateKeyPath);

        var keyBytes = File.ReadAllBytes(privateKeyPath);
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
    /// and propagates any exception when the file exists but is invalid.
    /// </returns>
    public static NodeIdentity TryLoad(string privateKeyPath, AuthorityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(privateKeyPath);
        ArgumentNullException.ThrowIfNull(registry);

        return File.Exists(privateKeyPath)
            ? Load(privateKeyPath, registry)
            : Anonymous();
    }
}
