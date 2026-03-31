using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GUNRPG.Security;

/// <summary>
/// Maintains the set of trusted authority public keys and provides lookup validation.
/// </summary>
/// <remarks>
/// <para>
/// Authority nodes are nodes that possess a private key whose public key is listed in this registry.
/// All other nodes are verifier nodes.
/// </para>
/// <para>
/// The registry is populated from a <c>config/authorities.json</c> file whose format is:
/// <code>
/// {
///   "authorities": [
///     "ed25519:&lt;hex-encoded-public-key&gt;",
///     ...
///   ]
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class AuthorityRegistry
{
    private const string Ed25519Prefix = "ed25519:";

    // Keys stored as lowercase hex strings for O(1) lookup.
    private readonly HashSet<string> _authorities;

    // Raw byte arrays retained so GetAuthorities() can return them defensively cloned.
    private readonly IReadOnlyList<byte[]> _authorityBytes;

    /// <summary>Initializes a registry with an explicit list of trusted public keys.</summary>
    /// <param name="authorityPublicKeys">
    /// The set of trusted Ed25519 public keys (each must be exactly 32 bytes).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="authorityPublicKeys"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any public key is not exactly 32 bytes.
    /// </exception>
    public AuthorityRegistry(IEnumerable<byte[]> authorityPublicKeys)
    {
        ArgumentNullException.ThrowIfNull(authorityPublicKeys);

        var hexSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byteList = new List<byte[]>();
        foreach (var key in authorityPublicKeys)
        {
            if (key is null)
                throw new ArgumentException("Authority public key must not be null.", nameof(authorityPublicKeys));
            if (key.Length != AuthorityCrypto.KeySize)
                throw new ArgumentException(
                    $"Authority public key must be exactly {AuthorityCrypto.KeySize} bytes.", nameof(authorityPublicKeys));
            var cloned = (byte[])key.Clone();
            hexSet.Add(ToHexKey(cloned));
            byteList.Add(cloned);
        }

        _authorities = hexSet;
        _authorityBytes = byteList;
    }

    /// <summary>An empty registry that trusts no authorities.</summary>
    public static AuthorityRegistry Empty { get; } = new AuthorityRegistry([]);

    /// <summary>
    /// Loads an <see cref="AuthorityRegistry"/> from a <c>config/authorities.json</c> file.
    /// </summary>
    /// <param name="path">
    /// Absolute or relative path to the JSON configuration file.
    /// </param>
    /// <returns>
    /// An <see cref="AuthorityRegistry"/> populated with the public keys listed in the file.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the file does not exist at <paramref name="path"/>.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when the file is not valid JSON, or a key entry is not in the expected
    /// <c>ed25519:&lt;hex&gt;</c> format, or a key does not decode to exactly 32 bytes.
    /// </exception>
    public static AuthorityRegistry LoadFromFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Authorities configuration file not found: {path}", path);

        var json = File.ReadAllText(path);
        var dto = JsonSerializer.Deserialize(json, AuthorityRegistryJsonContext.Default.AuthorityRegistryDto)
                  ?? throw new JsonException("Failed to deserialize authorities.json: result was null.");

        if (dto.Authorities is null)
            throw new JsonException("authorities.json must contain an 'authorities' array.");

        var keys = new List<byte[]>(dto.Authorities.Count);
        for (var i = 0; i < dto.Authorities.Count; i++)
        {
            var entry = dto.Authorities[i];
            if (entry is null)
                throw new JsonException($"authorities[{i}] must not be null.");

            keys.Add(ParsePublicKeyEntry(entry, i));
        }

        return new AuthorityRegistry(keys);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="publicKey"/> is a trusted authority.
    /// Lookup is O(1) via an internal hex-string <see cref="HashSet{T}"/>.
    /// </summary>
    /// <param name="publicKey">The Ed25519 public key to look up (must be exactly 32 bytes).</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="publicKey"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="publicKey"/> is not exactly 32 bytes.
    /// </exception>
    public bool IsTrustedAuthority(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != AuthorityCrypto.KeySize)
            throw new ArgumentException(
                $"Public key must be exactly {AuthorityCrypto.KeySize} bytes.", nameof(publicKey));

        return _authorities.Contains(ToHexKey(publicKey));
    }

    /// <summary>
    /// Returns a snapshot of all trusted authority public keys (each 32 bytes, defensively cloned).
    /// </summary>
    public IReadOnlyCollection<byte[]> GetAuthorities()
    {
        return _authorityBytes.Select(k => (byte[])k.Clone()).ToArray();
    }

    /// <summary>Converts a public key to a canonical lowercase hex string for HashSet storage.</summary>
    private static string ToHexKey(byte[] key) =>
        Convert.ToHexString(key).ToLowerInvariant();

    private static byte[] ParsePublicKeyEntry(string entry, int index)
    {
        if (!entry.StartsWith(Ed25519Prefix, StringComparison.OrdinalIgnoreCase))
            throw new JsonException(
                $"authorities[{index}] must start with '{Ed25519Prefix}'. Got: '{entry}'.");

        var hex = entry[Ed25519Prefix.Length..];
        byte[] key;
        try
        {
            key = Convert.FromHexString(hex);
        }
        catch (FormatException ex)
        {
            throw new JsonException(
                $"authorities[{index}] contains an invalid hex-encoded public key: '{hex}'.", ex);
        }

        if (key.Length != AuthorityCrypto.KeySize)
            throw new JsonException(
                $"authorities[{index}] must decode to exactly {AuthorityCrypto.KeySize} bytes, got {key.Length}.");

        return key;
    }
}

/// <summary>JSON DTO for <see cref="AuthorityRegistry"/> deserialization.</summary>
internal sealed record AuthorityRegistryDto(List<string>? Authorities);

[JsonSerializable(typeof(AuthorityRegistryDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AuthorityRegistryJsonContext : JsonSerializerContext;
