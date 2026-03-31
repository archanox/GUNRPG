using System.Security.Cryptography;
using System.Text.Json;
using GUNRPG.Security;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the Authority Key Trust Model:
/// - <see cref="AuthorityRegistry"/> load and lookup
/// - <see cref="NodeIdentity"/> role detection
/// - <see cref="AuthorityKeyGenerator"/> key pair generation
/// - <see cref="RunReceiptService.Create"/> with <see cref="AuthorityRole"/> enforcement
/// - <see cref="RunReceiptService.Verify"/> with <see cref="AuthorityRegistry"/> validation
/// </summary>
public sealed class AuthorityRegistryTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. AuthorityRegistry – construction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AuthorityRegistry_EmptyConstructor_HasNoAuthorities()
    {
        var registry = new AuthorityRegistry([]);
        Assert.Empty(registry.GetAuthorities());
    }

    [Fact]
    public void AuthorityRegistry_Empty_HasNoAuthorities()
    {
        Assert.Empty(AuthorityRegistry.Empty.GetAuthorities());
    }

    [Fact]
    public void AuthorityRegistry_WithKey_ContainsThatKey()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);

        Assert.Single(registry.GetAuthorities());
    }

    [Fact]
    public void AuthorityRegistry_NullKeyInCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AuthorityRegistry([null!]));
    }

    [Fact]
    public void AuthorityRegistry_WrongLengthKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AuthorityRegistry([new byte[16]]));
    }

    [Fact]
    public void AuthorityRegistry_GetAuthorities_ReturnsCopies()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);

        var keys1 = registry.GetAuthorities().ToArray();
        var keys2 = registry.GetAuthorities().ToArray();

        // Both calls must return equal content but different array instances.
        Assert.Equal(keys1.Single(), keys2.Single());
        Assert.NotSame(keys1.Single(), keys2.Single());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. AuthorityRegistry – IsTrustedAuthority lookup
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsTrustedAuthority_RegisteredKey_ReturnsTrue()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);

        Assert.True(registry.IsTrustedAuthority(publicKey));
    }

    [Fact]
    public void IsTrustedAuthority_UnregisteredKey_ReturnsFalse()
    {
        var (_, registeredKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, otherKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([registeredKey]);

        Assert.False(registry.IsTrustedAuthority(otherKey));
    }

    [Fact]
    public void IsTrustedAuthority_EmptyRegistry_ReturnsFalse()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();

        Assert.False(AuthorityRegistry.Empty.IsTrustedAuthority(publicKey));
    }

    [Fact]
    public void IsTrustedAuthority_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AuthorityRegistry.Empty.IsTrustedAuthority(null!));
    }

    [Fact]
    public void IsTrustedAuthority_WrongLengthKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => AuthorityRegistry.Empty.IsTrustedAuthority(new byte[16]));
    }

    [Fact]
    public void IsTrustedAuthority_MultipleKeys_FindsCorrectKey()
    {
        var (_, key1) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, key2) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, key3) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([key1, key2, key3]);

        Assert.True(registry.IsTrustedAuthority(key1));
        Assert.True(registry.IsTrustedAuthority(key2));
        Assert.True(registry.IsTrustedAuthority(key3));
    }

    [Fact]
    public void IsTrustedAuthority_MultipleKeys_UnknownKeyRejected()
    {
        var (_, key1) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, key2) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, key3) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, unknownKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([key1, key2, key3]);

        // All three registered keys must be found.
        Assert.True(registry.IsTrustedAuthority(key1));
        Assert.True(registry.IsTrustedAuthority(key2));
        Assert.True(registry.IsTrustedAuthority(key3));

        // A key that was never added must be rejected.
        Assert.False(registry.IsTrustedAuthority(unknownKey));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. AuthorityRegistry – LoadFromFile
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFile_EmptyAuthorities_LoadsEmptyRegistry()
    {
        using var tempPath = WriteJson("""{"authorities":[]}""");
        var registry = AuthorityRegistry.LoadFromFile(tempPath.Path);

        Assert.Empty(registry.GetAuthorities());
    }

    [Fact]
    public void LoadFromFile_ValidKey_LoadsKey()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var entry = AuthorityKeyGenerator.FormatPublicKeyEntry(publicKey);
        using var tempPath = WriteJson($$"""{"authorities":["{{entry}}"]}""");

        var registry = AuthorityRegistry.LoadFromFile(tempPath.Path);

        Assert.True(registry.IsTrustedAuthority(publicKey));
    }

    [Fact]
    public void LoadFromFile_MultipleKeys_LoadsAll()
    {
        var (_, key1) = AuthorityKeyGenerator.GenerateKeyPair();
        var (_, key2) = AuthorityKeyGenerator.GenerateKeyPair();
        var e1 = AuthorityKeyGenerator.FormatPublicKeyEntry(key1);
        var e2 = AuthorityKeyGenerator.FormatPublicKeyEntry(key2);
        using var tempPath = WriteJson($$"""{"authorities":["{{e1}}","{{e2}}"]}""");

        var registry = AuthorityRegistry.LoadFromFile(tempPath.Path);

        Assert.True(registry.IsTrustedAuthority(key1));
        Assert.True(registry.IsTrustedAuthority(key2));
    }

    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => AuthorityRegistry.LoadFromFile("/tmp/does-not-exist-gunrpg.json"));
    }

    [Fact]
    public void LoadFromFile_InvalidJson_ThrowsJsonException()
    {
        using var tempPath = WriteJson("not-valid-json");
        Assert.Throws<JsonException>(() => AuthorityRegistry.LoadFromFile(tempPath.Path));
    }

    [Fact]
    public void LoadFromFile_MissingPrefix_ThrowsJsonException()
    {
        // Key without the "ed25519:" prefix
        using var tempPath = WriteJson("""{"authorities":["abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890"]}""");
        Assert.Throws<JsonException>(() => AuthorityRegistry.LoadFromFile(tempPath.Path));
    }

    [Fact]
    public void LoadFromFile_InvalidHex_ThrowsJsonException()
    {
        using var tempPath = WriteJson("""{"authorities":["ed25519:not-valid-hex!"]}""");
        Assert.Throws<JsonException>(() => AuthorityRegistry.LoadFromFile(tempPath.Path));
    }

    [Fact]
    public void LoadFromFile_WrongKeyLength_ThrowsJsonException()
    {
        // Only 16 bytes (32 hex chars)
        using var tempPath = WriteJson("""{"authorities":["ed25519:deadbeefdeadbeefdeadbeefdeadbeef"]}""");
        Assert.Throws<JsonException>(() => AuthorityRegistry.LoadFromFile(tempPath.Path));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. NodeIdentity – role detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NodeIdentity_Anonymous_IsVerifier()
    {
        var identity = NodeIdentity.Anonymous();

        Assert.Equal(AuthorityRole.Verifier, identity.Role);
        Assert.False(identity.IsAuthority);
        Assert.Null(identity.PublicKey);
    }

    [Fact]
    public void NodeIdentity_Load_TrustedKey_IsAuthority()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);
        using var keyPath = WriteTempKeyFile(privateKey);

        var identity = NodeIdentity.Load(keyPath.Path, registry);

        Assert.Equal(AuthorityRole.Authority, identity.Role);
        Assert.True(identity.IsAuthority);
        Assert.NotNull(identity.PublicKey);
        Assert.Equal(publicKey, identity.PublicKey);
    }

    [Fact]
    public void NodeIdentity_Load_UntrustedKey_IsVerifier()
    {
        var (privateKey, _) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = AuthorityRegistry.Empty; // key is not registered
        using var keyPath = WriteTempKeyFile(privateKey);

        var identity = NodeIdentity.Load(keyPath.Path, registry);

        Assert.Equal(AuthorityRole.Verifier, identity.Role);
        Assert.False(identity.IsAuthority);
    }

    [Fact]
    public void NodeIdentity_Load_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(
            () => NodeIdentity.Load("/tmp/no-such-key-file.key", AuthorityRegistry.Empty));
    }

    [Fact]
    public void NodeIdentity_Load_WrongLengthKey_ThrowsArgumentException()
    {
        using var keyPath = WriteTempKeyFile(new byte[16]);
        Assert.Throws<ArgumentException>(() => NodeIdentity.Load(keyPath.Path, AuthorityRegistry.Empty));
    }

    [Fact]
    public void NodeIdentity_TryLoad_MissingFile_ReturnsAnonymous()
    {
        var identity = NodeIdentity.TryLoad("/tmp/no-such-key-file.key", AuthorityRegistry.Empty);

        Assert.Equal(AuthorityRole.Verifier, identity.Role);
        Assert.Null(identity.PublicKey);
    }

    [Fact]
    public void NodeIdentity_TryLoad_TrustedKey_IsAuthority()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);
        using var keyPath = WriteTempKeyFile(privateKey);

        var identity = NodeIdentity.TryLoad(keyPath.Path, registry);

        Assert.Equal(AuthorityRole.Authority, identity.Role);
    }

    [Fact]
    public void NodeIdentity_PublicKey_ReturnsCopy()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);
        using var keyPath = WriteTempKeyFile(privateKey);

        var identity = NodeIdentity.Load(keyPath.Path, registry);

        var copy1 = identity.PublicKey!;
        var copy2 = identity.PublicKey!;
        Assert.Equal(copy1, copy2);
        Assert.NotSame(copy1, copy2);
    }

    [Fact]
    public void NodeIdentity_GetFingerprint_AuthorityNode_ReturnsEd25519PrefixedShortHex()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var registry = new AuthorityRegistry([publicKey]);
        using var keyPath = WriteTempKeyFile(privateKey);

        var identity = NodeIdentity.Load(keyPath.Path, registry);
        var fingerprint = identity.GetFingerprint();

        Assert.NotNull(fingerprint);
        Assert.StartsWith("ed25519:", fingerprint, StringComparison.Ordinal);
        // "ed25519:" (8 chars) + 16 hex chars = 24 total
        Assert.Equal(24, fingerprint.Length);
    }

    [Fact]
    public void NodeIdentity_GetFingerprint_AnonymousNode_ReturnsNull()
    {
        var identity = NodeIdentity.Anonymous();

        Assert.Null(identity.GetFingerprint());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. AuthorityKeyGenerator
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateKeyPair_ProducesValidKeyLengths()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();

        Assert.Equal(32, privateKey.Length);
        Assert.Equal(32, publicKey.Length);
    }

    [Fact]
    public void GenerateKeyPair_TwoCalls_ProduceDifferentKeys()
    {
        var (priv1, pub1) = AuthorityKeyGenerator.GenerateKeyPair();
        var (priv2, pub2) = AuthorityKeyGenerator.GenerateKeyPair();

        Assert.False(priv1.AsSpan().SequenceEqual(priv2));
        Assert.False(pub1.AsSpan().SequenceEqual(pub2));
    }

    [Fact]
    public void FormatPublicKeyEntry_ProducesEd25519PrefixedHex()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var entry = AuthorityKeyGenerator.FormatPublicKeyEntry(publicKey);

        Assert.StartsWith("ed25519:", entry, StringComparison.Ordinal);
        // Remaining is 64 hex chars for 32 bytes
        Assert.Equal(8 + 64, entry.Length);
    }

    [Fact]
    public void FormatPublicKeyEntry_RoundTrips_ThroughRegistry()
    {
        var (_, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var entry = AuthorityKeyGenerator.FormatPublicKeyEntry(publicKey);
        using var tempPath = WriteJson($$"""{"authorities":["{{entry}}"]}""");

        var registry = AuthorityRegistry.LoadFromFile(tempPath.Path);

        Assert.True(registry.IsTrustedAuthority(publicKey));
    }

    [Fact]
    public void WriteKeyFiles_WritesCorrectBytes()
    {
        var (privateKey, publicKey) = AuthorityKeyGenerator.GenerateKeyPair();
        var privPath = Path.Combine(Path.GetTempPath(), $"test-priv-{Guid.NewGuid():N}.key");
        var pubPath = Path.Combine(Path.GetTempPath(), $"test-pub-{Guid.NewGuid():N}.key");

        try
        {
            AuthorityKeyGenerator.WriteKeyFiles(privPath, pubPath, privateKey, publicKey);

            Assert.Equal(privateKey, File.ReadAllBytes(privPath));
            Assert.Equal(publicKey, File.ReadAllBytes(pubPath));
        }
        finally
        {
            File.Delete(privPath);
            File.Delete(pubPath);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. RunReceiptService.Create – AuthorityRole enforcement
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithAuthorityRole_Succeeds()
    {
        var (authority, _, result) = BuildValidRun(seed: 100, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority, AuthorityRole.Authority);

        Assert.NotNull(receipt);
        Assert.True(receipt.IsStructurallyValid);
    }

    [Fact]
    public void Create_WithVerifierRole_ThrowsInvalidOperationException()
    {
        var (authority, _, result) = BuildValidRun(seed: 101, tickPositions: [0, 256, 512]);

        Assert.Throws<InvalidOperationException>(
            () => RunReceiptService.Create(result, authority, AuthorityRole.Verifier));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. RunReceiptService.Verify – AuthorityRegistry validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ValidSignatureKnownAuthority_ReturnsTrue()
    {
        var (authority, _, result) = BuildValidRun(seed: 110, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var authorityObj = authority.ToAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);

        Assert.True(RunReceiptService.Verify(receipt, authorityObj, registry));
    }

    [Fact]
    public void Verify_UnknownSigner_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 111, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var authorityObj = authority.ToAuthority();
        var emptyRegistry = AuthorityRegistry.Empty; // signer not registered

        Assert.False(RunReceiptService.Verify(receipt, authorityObj, emptyRegistry));
    }

    [Fact]
    public void Verify_InvalidSignature_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 112, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var authorityObj = authority.ToAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);

        // Tamper with the signature
        var tampered = new RunReceipt(
            receipt.SessionId,
            receipt.FinalTick,
            receipt.FinalStateHash,
            receipt.TickMerkleRoot,
            TamperBytes(receipt.Signature));

        Assert.False(RunReceiptService.Verify(tampered, authorityObj, registry));
    }

    [Fact]
    public void Verify_DifferentAuthorityKey_ReturnsFalse()
    {
        var (realAuthority, _, result) = BuildValidRun(seed: 113, tickPositions: [0, 256, 512]);
        var fakeAuthority = CreateAuthority("fake-authority");

        var receipt = RunReceiptService.Create(result, realAuthority);

        // Register the fake authority (unknown signer) only
        var registry = new AuthorityRegistry([fakeAuthority.PublicKey]);

        // Real authority's key is not in the registry
        Assert.False(RunReceiptService.Verify(receipt, realAuthority.ToAuthority(), registry));
    }

    [Fact]
    public void Verify_NullRegistry_Throws()
    {
        var (authority, _, result) = BuildValidRun(seed: 114, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var authorityObj = authority.ToAuthority();

        Assert.Throws<ArgumentNullException>(() =>
            RunReceiptService.Verify(receipt, authorityObj, null!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "registry-test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static (SessionAuthority authority, List<GUNRPG.Core.Simulation.SignedTick> ticks, SignedRunResult result)
        BuildValidRun(int seed, long[] tickPositions)
    {
        var sessionId = new Guid(seed, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        var playerId = Guid.NewGuid();
        var authority = CreateAuthority();

        var rng = new Random(seed);
        var ticks = new List<GUNRPG.Core.Simulation.SignedTick>();
        var stateHash = new byte[SHA256.HashSizeInBytes];
        rng.NextBytes(stateHash);

        foreach (var tick in tickPositions)
        {
            var prevState = (byte[])stateHash.Clone();
            rng.NextBytes(stateHash);
            var inputHash = new byte[SHA256.HashSizeInBytes];
            rng.NextBytes(inputHash);
            var sig = authority.SignTick(tick, prevState, stateHash, inputHash);
            ticks.Add(new GUNRPG.Core.Simulation.SignedTick(tick, prevState, stateHash, inputHash, sig));
        }

        var finalStateHash = (byte[])stateHash.Clone();
        var replayHash = new byte[SHA256.HashSizeInBytes];
        rng.NextBytes(replayHash);

        var merkleLeaves = ticks
            .Select(t => AuthorityCrypto.ComputeTickLeafHash(
                t.Tick, t.PrevStateHash, t.StateHash, t.InputHash))
            .ToList();
        var merkleRoot = GUNRPG.Security.MerkleTree.ComputeRoot(merkleLeaves);

        var checkpoints = tickPositions.Select((t, i) =>
        {
            var hash = new byte[SHA256.HashSizeInBytes];
            rng.NextBytes(hash);
            return new GUNRPG.Security.RunCheckpoint(t, hash);
        }).ToList();

        var result = authority.Sign(sessionId, playerId, finalStateHash, replayHash, merkleRoot, checkpoints);
        return (authority, ticks, result);
    }

    private static TempFile WriteJson(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gunrpg-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return new TempFile(path);
    }

    private static TempFile WriteTempKeyFile(byte[] keyBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gunrpg-test-key-{Guid.NewGuid():N}.key");
        File.WriteAllBytes(path, keyBytes);
        return new TempFile(path);
    }

    /// <summary>Deletes a temporary file when disposed.</summary>
    private sealed class TempFile : IDisposable
    {
        public TempFile(string path) { Path = path; }
        public string Path { get; }
        public void Dispose()
        {
            try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static byte[] TamperBytes(byte[] bytes)
    {
        var copy = (byte[])bytes.Clone();
        copy[0] ^= 0xFF;
        return copy;
    }
}
