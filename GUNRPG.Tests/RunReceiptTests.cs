using System.Security.Cryptography;
using System.Text.Json;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for Proof-of-Run Receipts:
/// - <see cref="RunReceipt"/> model safety requirements
/// - <see cref="RunReceiptService.Create"/> receipt generation
/// - <see cref="RunReceiptService.Verify"/> acceptance and rejection
/// - JSON serialization round-trip
/// - Third-party verification without replay data
/// </summary>
public sealed class RunReceiptTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. RunReceipt model – structural validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RunReceipt_ValidFields_IsStructurallyValid()
    {
        var receipt = new RunReceipt(
            SessionId: Guid.NewGuid(),
            FinalTick: 100,
            FinalStateHash: CreateHash(1),
            TickMerkleRoot: CreateHash(2),
            Signature: CreateSignatureBytes(3));

        Assert.True(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_NullFinalStateHash_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, null!, CreateHash(2), CreateSignatureBytes(3));
        Assert.False(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_WrongLengthFinalStateHash_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, new byte[16], CreateHash(2), CreateSignatureBytes(3));
        Assert.False(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_NullTickMerkleRoot_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, CreateHash(1), null!, CreateSignatureBytes(3));
        Assert.False(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_WrongLengthTickMerkleRoot_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, CreateHash(1), new byte[16], CreateSignatureBytes(3));
        Assert.False(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_NullSignature_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, CreateHash(1), CreateHash(2), null!);
        Assert.False(receipt.IsStructurallyValid);
    }

    [Fact]
    public void RunReceipt_WrongLengthSignature_NotStructurallyValid()
    {
        var receipt = new RunReceipt(Guid.NewGuid(), 0, CreateHash(1), CreateHash(2), new byte[16]);
        Assert.False(receipt.IsStructurallyValid);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Receipt creation – valid run produces a receipt
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ValidRunWithCheckpoints_ProducesReceipt()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 1, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority);

        Assert.NotNull(receipt);
        Assert.Equal(result.SessionId, receipt.SessionId);
        Assert.Equal(32, receipt.FinalStateHash.Length);
        Assert.Equal(32, receipt.TickMerkleRoot.Length);
        Assert.NotNull(receipt.Signature);
        Assert.Equal(64, receipt.Signature.Length);
        Assert.True(receipt.IsStructurallyValid);
    }

    [Fact]
    public void Create_ValidRun_FinalTickMatchesLastCheckpoint()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 2, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority);

        Assert.Equal(result.Checkpoints![^1].TickIndex, receipt.FinalTick);
    }

    [Fact]
    public void Create_ValidRun_FinalStateHashMatchesRunFinalHash()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 3, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority);

        var expectedFinalStateHash = Convert.FromHexString(result.FinalHash);
        Assert.Equal(expectedFinalStateHash, receipt.FinalStateHash);
    }

    [Fact]
    public void Create_ValidRun_TickMerkleRootMatchesRunTickMerkleRoot()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 4, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority);

        var expectedMerkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        Assert.Equal(expectedMerkleRoot, receipt.TickMerkleRoot);
    }

    [Fact]
    public void Create_RunWithoutTickMerkleRoot_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(10);
        var replayHash = CreateHash(11);

        // Sign without TickMerkleRoot
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash);

        Assert.Throws<ArgumentException>(() => RunReceiptService.Create(result, authority));
    }

    [Fact]
    public void Create_NullRun_Throws()
    {
        var authority = CreateAuthority();
        Assert.Throws<ArgumentNullException>(() => RunReceiptService.Create(null!, authority));
    }

    [Fact]
    public void Create_NullAuthority_Throws()
    {
        var (_, _, result) = BuildValidRun(seed: 5, tickPositions: [0, 256, 512]);
        Assert.Throws<ArgumentNullException>(() => RunReceiptService.Create(result, null!));
    }

    [Fact]
    public void Create_RunWithMerkleButNoCheckpoints_UsesFinalTick0()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(10);
        var replayHash = CreateHash(11);
        var merkleRoot = CreateHash(12);

        // Sign with TickMerkleRoot but no checkpoints
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash, merkleRoot);

        var receipt = RunReceiptService.Create(result, authority);

        // FinalTick defaults to 0 when no checkpoints are present
        Assert.Equal(0L, receipt.FinalTick);
        Assert.True(receipt.IsStructurallyValid);
    }

    [Fact]
    public void Create_DifferentRuns_ProduceDifferentReceipts()
    {
        var authority = CreateAuthority();

        var (_, _, result1) = BuildValidRun(seed: 10, tickPositions: [0, 256, 512]);
        var (_, _, result2) = BuildValidRun(seed: 11, tickPositions: [0, 256, 512]);

        var receipt1 = RunReceiptService.Create(result1, authority);
        var receipt2 = RunReceiptService.Create(result2, authority);

        // Different session IDs means different receipts
        Assert.NotEqual(receipt1.SessionId, receipt2.SessionId);
        // Signatures must differ for different payloads
        Assert.False(receipt1.Signature.AsSpan().SequenceEqual(receipt2.Signature));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Receipt verification – valid receipt verifies
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ValidReceipt_ReturnsTrue()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 20, tickPositions: [0, 256, 512]);

        var receipt = RunReceiptService.Create(result, authority);

        Assert.True(RunReceiptService.Verify(receipt, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ValidReceipt_ManyCheckpoints_ReturnsTrue()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 21, tickPositions: [0, 256, 512, 768, 1024]);

        var receipt = RunReceiptService.Create(result, authority);

        Assert.True(RunReceiptService.Verify(receipt, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ReceiptFromMerkleOnlyRun_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(30);
        var replayHash = CreateHash(31);
        var merkleRoot = CreateHash(32);
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash, merkleRoot);

        var receipt = RunReceiptService.Create(result, authority);

        Assert.True(RunReceiptService.Verify(receipt, authority.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Receipt verification – tampered receipt rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ModifiedSignature_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 30, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tamperedSig = (byte[])receipt.Signature.Clone();
        tamperedSig[0] ^= 0xFF;
        var tampered = receipt with { Signature = tamperedSig };

        Assert.False(RunReceiptService.Verify(tampered, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ModifiedFinalStateHash_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 31, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tampered = receipt with { FinalStateHash = TamperHash(receipt.FinalStateHash) };

        Assert.False(RunReceiptService.Verify(tampered, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ModifiedTickMerkleRoot_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 32, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tampered = receipt with { TickMerkleRoot = TamperHash(receipt.TickMerkleRoot) };

        Assert.False(RunReceiptService.Verify(tampered, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ModifiedSessionId_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 33, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tampered = receipt with { SessionId = Guid.NewGuid() };

        Assert.False(RunReceiptService.Verify(tampered, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ModifiedFinalTick_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 34, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tampered = receipt with { FinalTick = receipt.FinalTick + 1 };

        Assert.False(RunReceiptService.Verify(tampered, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_WrongAuthority_ReturnsFalse()
    {
        var (authority, ticks, result) = BuildValidRun(seed: 35, tickPositions: [0, 256, 512]);
        var wrongAuthority = CreateAuthority("wrong-authority");

        var receipt = RunReceiptService.Create(result, authority);

        Assert.False(RunReceiptService.Verify(receipt, wrongAuthority.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Receipt verification – malformed receipts rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_NullFinalStateHash_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 40, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var malformed = receipt with { FinalStateHash = null! };

        Assert.False(RunReceiptService.Verify(malformed, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ShortFinalStateHash_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 41, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var malformed = receipt with { FinalStateHash = new byte[16] };

        Assert.False(RunReceiptService.Verify(malformed, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_NullTickMerkleRoot_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 42, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var malformed = receipt with { TickMerkleRoot = null! };

        Assert.False(RunReceiptService.Verify(malformed, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_ShortTickMerkleRoot_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 43, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var malformed = receipt with { TickMerkleRoot = new byte[16] };

        Assert.False(RunReceiptService.Verify(malformed, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_NullSignature_ReturnsFalse()
    {
        var (authority, _, result) = BuildValidRun(seed: 44, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var malformed = receipt with { Signature = null! };

        Assert.False(RunReceiptService.Verify(malformed, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_NullReceipt_Throws()
    {
        var authority = CreateAuthority();
        Assert.Throws<ArgumentNullException>(() => RunReceiptService.Verify(null!, authority.ToAuthority()));
    }

    [Fact]
    public void Verify_NullAuthority_Throws()
    {
        var (authority, _, result) = BuildValidRun(seed: 45, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        Assert.Throws<ArgumentNullException>(() => RunReceiptService.Verify(receipt, null!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. JSON serialization round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ToJson_FromJson_RoundTrip_ProducesEqualReceipt()
    {
        var (authority, _, result) = BuildValidRun(seed: 50, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var json = receipt.ToJson();
        var restored = RunReceipt.FromJson(json);

        Assert.Equal(receipt.SessionId, restored.SessionId);
        Assert.Equal(receipt.FinalTick, restored.FinalTick);
        Assert.Equal(receipt.FinalStateHash, restored.FinalStateHash);
        Assert.Equal(receipt.TickMerkleRoot, restored.TickMerkleRoot);
        Assert.Equal(receipt.Signature, restored.Signature);
    }

    [Fact]
    public void ToJson_ProducesValidJson()
    {
        var (authority, _, result) = BuildValidRun(seed: 51, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var json = receipt.ToJson();

        // Should be parseable as JSON and contain required fields
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("sessionId", out _), "JSON must contain 'sessionId'");
        Assert.True(root.TryGetProperty("finalTick", out _), "JSON must contain 'finalTick'");
        Assert.True(root.TryGetProperty("finalStateHash", out _), "JSON must contain 'finalStateHash'");
        Assert.True(root.TryGetProperty("tickMerkleRoot", out _), "JSON must contain 'tickMerkleRoot'");
        Assert.True(root.TryGetProperty("signature", out _), "JSON must contain 'signature'");
    }

    [Fact]
    public void FromJson_RestoredReceipt_VerifiesSuccessfully()
    {
        var (authority, _, result) = BuildValidRun(seed: 52, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var json = receipt.ToJson();
        var restored = RunReceipt.FromJson(json);

        Assert.True(RunReceiptService.Verify(restored, authority.ToAuthority()));
    }

    [Fact]
    public void ToJson_ProducesCompactPayload()
    {
        var (authority, _, result) = BuildValidRun(seed: 53, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var json = receipt.ToJson();

        // Receipt size: 16 (guid) + 8 (finalTick) + 32 (stateHash) + 32 (merkleRoot) + 64 (signature)
        // plus JSON overhead. Should stay well under 1KB.
        Assert.True(json.Length < 1024, $"Receipt JSON is too large: {json.Length} chars");
    }

    [Fact]
    public void FromJson_InvalidJson_Throws()
    {
        Assert.Throws<JsonException>(() => RunReceipt.FromJson("not-valid-json"));
    }

    [Fact]
    public void FromJson_NullJson_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RunReceipt.FromJson(null!));
    }

    [Fact]
    public void FromJson_TruncatedFinalStateHash_ThrowsJsonException()
    {
        var (authority, _, result) = BuildValidRun(seed: 54, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        // Build JSON manually with a too-short finalStateHash (16 bytes instead of 32)
        var tamperedJson = BuildReceiptJson(
            receipt.SessionId, receipt.FinalTick,
            finalStateHashBase64: Convert.ToBase64String(new byte[16]),
            tickMerkleRootBase64: Convert.ToBase64String(receipt.TickMerkleRoot),
            signatureBase64: Convert.ToBase64String(receipt.Signature));

        var ex = Assert.Throws<JsonException>(() => RunReceipt.FromJson(tamperedJson));
        Assert.Contains("finalStateHash", ex.Message);
    }

    [Fact]
    public void FromJson_TruncatedTickMerkleRoot_ThrowsJsonException()
    {
        var (authority, _, result) = BuildValidRun(seed: 55, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        // Build JSON manually with a too-short tickMerkleRoot (16 bytes instead of 32)
        var tamperedJson = BuildReceiptJson(
            receipt.SessionId, receipt.FinalTick,
            finalStateHashBase64: Convert.ToBase64String(receipt.FinalStateHash),
            tickMerkleRootBase64: Convert.ToBase64String(new byte[16]),
            signatureBase64: Convert.ToBase64String(receipt.Signature));

        var ex = Assert.Throws<JsonException>(() => RunReceipt.FromJson(tamperedJson));
        Assert.Contains("tickMerkleRoot", ex.Message);
    }

    [Fact]
    public void FromJson_TruncatedSignature_ThrowsJsonException()
    {
        var (authority, _, result) = BuildValidRun(seed: 56, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        // Build JSON manually with a too-short signature (16 bytes instead of 64)
        var tamperedJson = BuildReceiptJson(
            receipt.SessionId, receipt.FinalTick,
            finalStateHashBase64: Convert.ToBase64String(receipt.FinalStateHash),
            tickMerkleRootBase64: Convert.ToBase64String(receipt.TickMerkleRoot),
            signatureBase64: Convert.ToBase64String(new byte[16]));

        var ex = Assert.Throws<JsonException>(() => RunReceipt.FromJson(tamperedJson));
        Assert.Contains("signature", ex.Message);
    }

    [Fact]
    public void FromJson_InvalidBase64_ThrowsJsonException()
    {
        var (authority, _, result) = BuildValidRun(seed: 57, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);

        var tamperedJson = BuildReceiptJson(
            receipt.SessionId, receipt.FinalTick,
            finalStateHashBase64: "!!! not base64 !!!",
            tickMerkleRootBase64: Convert.ToBase64String(receipt.TickMerkleRoot),
            signatureBase64: Convert.ToBase64String(receipt.Signature));

        Assert.Throws<JsonException>(() => RunReceipt.FromJson(tamperedJson));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Third-party verification (no replay required)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThirdParty_ServerCreatesReceipt_PlayerPublishes_ThirdPartyVerifies()
    {
        // ── Server: verifies run and creates receipt ──
        var (serverAuthority, ticks, result) = BuildValidRun(seed: 60, tickPositions: [0, 256, 512]);
        var serverVerifier = new ReplayVerifier(serverAuthority.ToAuthority());
        var simulation = new SimpleSimulation(seed: 60);

        var isValid = serverVerifier.VerifyRun(ticks, result, simulation);
        Assert.True(isValid, "Server: run must verify successfully.");

        var receipt = RunReceiptService.Create(result, serverAuthority);

        // ── Player: publishes receipt as JSON ──
        var publishedJson = receipt.ToJson();

        // ── Third party: verifies receipt with only the public key (no replay) ──
        var authorityPublicKey = serverAuthority.ToAuthority();
        var restoredReceipt = RunReceipt.FromJson(publishedJson);
        var isReceiptValid = RunReceiptService.Verify(restoredReceipt, authorityPublicKey);

        Assert.True(isReceiptValid, "Third party must verify the receipt using only the public key.");
    }

    [Fact]
    public void ThirdParty_TamperedReceipt_Rejected()
    {
        var (serverAuthority, ticks, result) = BuildValidRun(seed: 61, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, serverAuthority);

        // Player tampers with the final tick value
        var tampered = receipt with { FinalTick = receipt.FinalTick + 999 };
        var publishedJson = tampered.ToJson();

        // Third party tries to verify the tampered receipt
        var authorityPublicKey = serverAuthority.ToAuthority();
        var restoredReceipt = RunReceipt.FromJson(publishedJson);
        var isValid = RunReceiptService.Verify(restoredReceipt, authorityPublicKey);

        Assert.False(isValid, "Third party must reject a tampered receipt.");
    }

    [Fact]
    public void ThirdParty_ReceiptFromDifferentAuthority_Rejected()
    {
        var (realAuthority, _, result) = BuildValidRun(seed: 62, tickPositions: [0, 256, 512]);
        var fakeAuthority = CreateAuthority("fake-authority");

        // Fake authority creates a receipt for the real run (but signs with wrong key)
        var fakeReceipt = RunReceiptService.Create(result, fakeAuthority);

        // Third party uses the real authority's public key
        var isValid = RunReceiptService.Verify(fakeReceipt, realAuthority.ToAuthority());

        Assert.False(isValid, "Third party must reject a receipt signed by a different authority.");
    }

    [Fact]
    public void ThirdParty_SameReceiptVerifiesMultipleTimes()
    {
        var (authority, _, result) = BuildValidRun(seed: 63, tickPositions: [0, 256, 512]);
        var receipt = RunReceiptService.Create(result, authority);
        var authorityPublicKey = authority.ToAuthority();

        // Multiple verification calls on the same receipt must all succeed
        for (var i = 0; i < 5; i++)
        {
            Assert.True(RunReceiptService.Verify(receipt, authorityPublicKey),
                $"Verification #{i + 1} must succeed.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. SessionAuthority.CreateRunReceipt directly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionAuthority_CreateRunReceipt_NullRun_Throws()
    {
        var authority = CreateAuthority();
        Assert.Throws<ArgumentNullException>(() => authority.CreateRunReceipt(null!));
    }

    [Fact]
    public void SessionAuthority_CreateRunReceipt_RunWithoutMerkleRoot_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(1);
        var replayHash = CreateHash(2);
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash);

        Assert.Throws<ArgumentException>(() => authority.CreateRunReceipt(result));
    }

    [Fact]
    public void SessionAuthority_CreateRunReceipt_DirectlyEquivalentToServiceCreate()
    {
        var (authority, _, result) = BuildValidRun(seed: 70, tickPositions: [0, 256, 512]);

        var receiptDirect = authority.CreateRunReceipt(result);
        var receiptViaService = RunReceiptService.Create(result, authority);

        // Both must verify successfully against the same authority
        var authorityPublicKey = authority.ToAuthority();
        Assert.True(RunReceiptService.Verify(receiptDirect, authorityPublicKey));
        Assert.True(RunReceiptService.Verify(receiptViaService, authorityPublicKey));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "receipt-test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[SHA256.HashSizeInBytes];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xEE;
        return hash;
    }

    /// <summary>
    /// Creates a 64-byte buffer for structural-validation tests only.
    /// The bytes are <em>not</em> a valid Ed25519 signature and must never be passed
    /// to <see cref="RunReceiptService.Verify"/>.
    /// </summary>
    private static byte[] CreateSignatureBytes(int seed)
    {
        var sig = new byte[64];
        sig[0] = (byte)(seed & 0xFF);
        sig[63] = 0xCC;
        return sig;
    }

    /// <summary>Flips the first byte of a hash copy to simulate tampering.</summary>
    private static byte[] TamperHash(byte[] hash)
    {
        var tampered = (byte[])hash.Clone();
        tampered[0] ^= 0xFF;
        return tampered;
    }

    /// <summary>
    /// Builds a receipt JSON string directly from components, bypassing <see cref="RunReceipt.ToJson"/>.
    /// Used by tamper tests to construct receipts with invalid field values.
    /// </summary>
    private static string BuildReceiptJson(
        Guid sessionId,
        long finalTick,
        string finalStateHashBase64,
        string tickMerkleRootBase64,
        string signatureBase64)
    {
        return $"{{\"sessionId\":\"{sessionId}\",\"finalTick\":{finalTick},"
               + $"\"finalStateHash\":\"{finalStateHashBase64}\","
               + $"\"tickMerkleRoot\":\"{tickMerkleRootBase64}\","
               + $"\"signature\":\"{signatureBase64}\"}}";
    }

    /// <summary>
    /// Builds a valid run with checkpoints using <see cref="TickAuthorityService.FinalizeRun"/>.
    /// </summary>
    private static (SessionAuthority Authority, List<SignedTick> Ticks, SignedRunResult Result)
        BuildValidRun(int seed, long[] tickPositions)
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(seed);
        var ticks = BuildChainAtPositions(authority, state, tickPositions);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var replayHash = CreateHash(seed);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);
        return (authority, ticks, result);
    }

    private static List<SignedTick> BuildChainAtPositions(
        SessionAuthority authority,
        SimulationState state,
        long[] tickPositions)
    {
        var hasher = new StateHasher();
        var ticks = new List<SignedTick>(tickPositions.Length);
        var prev = TickAuthorityService.GenesisStateHash;

        foreach (var tickPos in tickPositions)
        {
            var stateHash = hasher.HashTick(tickPos, state);
            var inputHash = new TickInputs(tickPos, [new PlayerInput(TestPlayerId, new ExfilAction())]).ComputeHash();
            var sig = authority.SignTick(tickPos, prev, stateHash, inputHash);
            ticks.Add(new SignedTick(tickPos, prev, stateHash, inputHash, sig));
            prev = stateHash;
        }

        return ticks;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Simple simulation for verifying runs in integration tests
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Honest simulation that computes state hashes using <see cref="StateHasher"/>
    /// with a fixed simulation state.
    /// </summary>
    private sealed class SimpleSimulation : IDeterministicSimulation
    {
        private readonly SimulationState _state;
        private readonly StateHasher _hasher = new();
        private byte[] _currentHash = new byte[32];
        private long _currentTick = -1;

        public SimpleSimulation(int seed) => _state = ReplayRunner.CreateInitialState(seed);

        public void Reset()
        {
            _currentHash = new byte[32];
            _currentTick = -1;
        }

        public void ApplyTick(SignedTick tick)
        {
            _currentHash = _hasher.HashTick(tick.Tick, _state);
            _currentTick = tick.Tick;
        }

        public byte[] GetStateHash() => (byte[])_currentHash.Clone();

        public byte[] SerializeState()
        {
            var bytes = new byte[8 + 32];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(0, 8), _currentTick);
            _currentHash.CopyTo(bytes, 8);
            return bytes;
        }

        public void LoadState(byte[] state)
        {
            _currentTick = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(state.AsSpan(0, 8));
            _currentHash = state[8..40];
        }
    }
}
