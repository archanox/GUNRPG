using System.Text.Json;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for Merkle checkpoint proofs:
/// - <see cref="MerkleCheckpointProof"/> record and <see cref="MerkleCheckpointProof.IsStructurallyValid"/>
/// - <see cref="MerkleCheckpointProof.VerifyCheckpointProof"/> – proof verification
/// - <see cref="MerkleTree.BuildCheckpointProof"/> – proof generation
/// - <see cref="MerkleCheckpoint"/> extended with <see cref="MerkleCheckpoint.Proof"/>
/// - <see cref="SessionAuthority.VerifyMerkleCheckpoint"/> with optional <c>expectedMerkleRoot</c>
/// - <see cref="TickAuthorityService.BuildCheckpointProof"/> – proof from tick chain
/// - JSON round-trip for <see cref="MerkleCheckpoint"/> with proof
/// </summary>
public sealed class MerkleCheckpointProofTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. MerkleTree.BuildCheckpointProof + VerifyCheckpointProof round-trips
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    [InlineData(4, 3)]
    [InlineData(7, 0)]
    [InlineData(7, 3)]
    [InlineData(7, 6)]
    [InlineData(8, 0)]
    [InlineData(8, 7)]
    [InlineData(16, 0)]
    [InlineData(16, 15)]
    [InlineData(17, 0)]
    [InlineData(17, 8)]
    [InlineData(17, 16)]
    public void BuildCheckpointProof_VerifyCheckpointProof_RoundTrip(int leafCount, int eventIndex)
    {
        var leaves = BuildLeaves(leafCount);
        var root = MerkleTree.ComputeRoot(leaves);

        var proof = MerkleTree.BuildCheckpointProof(leaves, eventIndex);

        Assert.Equal(eventIndex, proof.EventIndex);
        Assert.True(proof.LeafHash.AsSpan().SequenceEqual(leaves[eventIndex]),
            "LeafHash must be the raw leaf data for the specified event index.");
        Assert.True(proof.IsStructurallyValid, "Generated proof must be structurally valid.");
        Assert.True(MerkleCheckpointProof.VerifyCheckpointProof(proof, root),
            "Proof must verify against the tree root.");
    }

    [Fact]
    public void BuildCheckpointProof_SingleLeaf_VerifiesAgainstRoot()
    {
        var leaf = CreateHash(1);
        var root = MerkleTree.ComputeRoot([leaf]);

        var proof = MerkleTree.BuildCheckpointProof([leaf], 0);

        Assert.Empty(proof.SiblingHashes);
        Assert.True(MerkleCheckpointProof.VerifyCheckpointProof(proof, root));
    }

    [Fact]
    public void BuildCheckpointProof_AllIndices_VerifyAgainstSameRoot()
    {
        const int count = 10;
        var leaves = BuildLeaves(count);
        var root = MerkleTree.ComputeRoot(leaves);

        for (var i = 0; i < count; i++)
        {
            var proof = MerkleTree.BuildCheckpointProof(leaves, i);
            Assert.True(MerkleCheckpointProof.VerifyCheckpointProof(proof, root),
                $"Proof for event index {i} must verify against the tree root.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Tamper detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyCheckpointProof_TamperedLeafHash_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 1);

        var tamperedLeaf = (byte[])proof.LeafHash.Clone();
        tamperedLeaf[0] ^= 0xFF;
        var tampered = new MerkleCheckpointProof(proof.EventIndex, tamperedLeaf, proof.SiblingHashes);

        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(tampered, root));
    }

    [Fact]
    public void VerifyCheckpointProof_TamperedSiblingHash_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 0);

        var tamperedSiblings = proof.SiblingHashes
            .Select((s, i) => i == 0 ? TamperByte(s) : (byte[])s.Clone())
            .ToList<byte[]>();
        var tampered = new MerkleCheckpointProof(proof.EventIndex, proof.LeafHash, tamperedSiblings);

        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(tampered, root));
    }

    [Fact]
    public void VerifyCheckpointProof_WrongRoot_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 2);

        var wrongRoot = TamperByte(root);

        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(proof, wrongRoot));
    }

    [Fact]
    public void VerifyCheckpointProof_ProofFromDifferentLeafSet_ReturnsFalse()
    {
        var leaves1 = BuildLeaves(4);
        var leaves2 = BuildLeaves(4);
        leaves2[2][0] ^= 0xFF; // tamper leaf 2 in second set
        var root1 = MerkleTree.ComputeRoot(leaves1);

        // Proof built for leaves2 at index 2 – then verified against the root from leaves1
        var proof = MerkleTree.BuildCheckpointProof(leaves2, 2);

        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(proof, root1));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. IsStructurallyValid
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsStructurallyValid_ValidProof_ReturnsTrue()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), [CreateHash(2)]);
        Assert.True(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_NegativeEventIndex_ReturnsFalse()
    {
        var proof = new MerkleCheckpointProof(-1, CreateHash(1), []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_NullLeafHash_ReturnsFalse()
    {
        var proof = new MerkleCheckpointProof(0, null!, []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_WrongLengthLeafHash_ReturnsFalse()
    {
        var proof = new MerkleCheckpointProof(0, new byte[16], []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_NullSiblingHashes_ReturnsFalse()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), null!);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_SiblingHashWrongLength_ReturnsFalse()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), [new byte[16]]);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void IsStructurallyValid_EmptySiblingHashes_ReturnsTrue()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), []);
        Assert.True(proof.IsStructurallyValid);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. VerifyCheckpointProof – argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyCheckpointProof_NullProof_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MerkleCheckpointProof.VerifyCheckpointProof(null!, CreateHash(1)));
    }

    [Fact]
    public void VerifyCheckpointProof_NullExpectedRoot_Throws()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), []);
        Assert.Throws<ArgumentNullException>(() =>
            MerkleCheckpointProof.VerifyCheckpointProof(proof, null!));
    }

    [Fact]
    public void VerifyCheckpointProof_WrongLengthRoot_Throws()
    {
        var proof = new MerkleCheckpointProof(0, CreateHash(1), []);
        Assert.Throws<ArgumentException>(() =>
            MerkleCheckpointProof.VerifyCheckpointProof(proof, new byte[16]));
    }

    [Fact]
    public void VerifyCheckpointProof_InvalidStructure_ReturnsFalse()
    {
        var root = CreateHash(99);
        // LeafHash is wrong length → proof is not structurally valid → returns false
        var proof = new MerkleCheckpointProof(0, new byte[16], []);
        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(proof, root));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. MerkleTree.BuildCheckpointProof – argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildCheckpointProof_NullLeaves_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MerkleTree.BuildCheckpointProof(null!, 0));
    }

    [Fact]
    public void BuildCheckpointProof_NegativeIndex_Throws()
    {
        var leaves = BuildLeaves(4);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MerkleTree.BuildCheckpointProof(leaves, -1));
    }

    [Fact]
    public void BuildCheckpointProof_IndexOutOfRange_Throws()
    {
        var leaves = BuildLeaves(4);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MerkleTree.BuildCheckpointProof(leaves, 4));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. MerkleCheckpoint with Proof – structure and JSON round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MerkleCheckpoint_DefaultProof_IsNull()
    {
        var authority = CreateAuthority();
        var checkpoint = authority.CreateMerkleCheckpoint(1024, CreateHash(10));

        Assert.Null(checkpoint.Proof);
    }

    [Fact]
    public void MerkleCheckpoint_WithProof_PreservesProof()
    {
        var authority = CreateAuthority();
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 2);

        var checkpoint = new MerkleCheckpoint(1024, root, authority.PublicKey,
            authority.CreateMerkleCheckpoint(1024, root).Signature, proof);

        Assert.NotNull(checkpoint.Proof);
        Assert.Equal(2L, checkpoint.Proof!.EventIndex);
        Assert.True(checkpoint.Proof.LeafHash.AsSpan().SequenceEqual(leaves[2]));
    }

    [Fact]
    public void MerkleCheckpoint_ToJsonBytes_FromJsonBytes_WithProof_RoundTrip()
    {
        var authority = CreateAuthority();
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 1);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(2048, root);
        var original = new MerkleCheckpoint(
            innerCheckpoint.Tick,
            innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey,
            innerCheckpoint.Signature,
            proof);

        var json = original.ToJsonBytes();
        var restored = MerkleCheckpoint.FromJsonBytes(json);

        Assert.Equal(original.Tick, restored.Tick);
        Assert.True(original.MerkleRoot.AsSpan().SequenceEqual(restored.MerkleRoot));
        Assert.True(original.AuthorityPublicKey.AsSpan().SequenceEqual(restored.AuthorityPublicKey));
        Assert.True(original.Signature.AsSpan().SequenceEqual(restored.Signature));

        Assert.NotNull(restored.Proof);
        Assert.Equal(proof.EventIndex, restored.Proof!.EventIndex);
        Assert.True(proof.LeafHash.AsSpan().SequenceEqual(restored.Proof.LeafHash));
        Assert.Equal(proof.SiblingHashes.Count, restored.Proof.SiblingHashes.Count);
        for (var i = 0; i < proof.SiblingHashes.Count; i++)
            Assert.True(proof.SiblingHashes[i].AsSpan().SequenceEqual(restored.Proof.SiblingHashes[i]),
                $"SiblingHashes[{i}] must survive JSON round-trip.");

        // The restored proof must still verify against the root.
        Assert.True(MerkleCheckpointProof.VerifyCheckpointProof(restored.Proof, root));
    }

    [Fact]
    public void MerkleCheckpoint_ToJsonBytes_FromJsonBytes_WithoutProof_ProofIsNull()
    {
        var authority = CreateAuthority();
        var original = authority.CreateMerkleCheckpoint(512, CreateHash(20));

        var json = original.ToJsonBytes();
        var restored = MerkleCheckpoint.FromJsonBytes(json);

        Assert.Null(restored.Proof);
    }

    [Fact]
    public void MerkleCheckpoint_FromJsonBytes_MalformedProofLeafHash_ThrowsJsonException()
    {
        // Manually inject a bad base-64 leafHash in the proof JSON.
        var authority = CreateAuthority();
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 0);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(0, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, proof);
        var json = System.Text.Encoding.UTF8.GetString(checkpoint.ToJsonBytes());

        // Corrupt the leafHash value in the JSON.
        var corrupted = json.Replace(Convert.ToBase64String(proof.LeafHash), "NOT_BASE64!!!");
        Assert.Throws<JsonException>(() =>
            MerkleCheckpoint.FromJsonBytes(System.Text.Encoding.UTF8.GetBytes(corrupted)));
    }

    [Fact]
    public void MerkleCheckpoint_FromJsonBytes_NegativeEventIndex_ThrowsJsonException()
    {
        // Manually inject a negative eventIndex in the proof JSON.
        var authority = CreateAuthority();
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 0);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(0, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, proof);
        var json = System.Text.Encoding.UTF8.GetString(checkpoint.ToJsonBytes());

        // Replace "eventIndex":0 with "eventIndex":-1.
        var corrupted = json.Replace("\"eventIndex\":0", "\"eventIndex\":-1");
        Assert.Throws<JsonException>(() =>
            MerkleCheckpoint.FromJsonBytes(System.Text.Encoding.UTF8.GetBytes(corrupted)));
    }

    [Fact]
    public void MerkleCheckpoint_FromJsonBytes_ExcessiveSiblingCount_ThrowsJsonException()
    {
        // Manually inject a siblingHashes array that exceeds MaxMerkleProofDepth.
        var authority = CreateAuthority();
        var root = CreateHash(77);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(0, root);
        // Build raw JSON with more siblings than MaxMerkleProofDepth (64).
        var validRoot = Convert.ToBase64String(innerCheckpoint.MerkleRoot);
        var validKey = Convert.ToBase64String(innerCheckpoint.AuthorityPublicKey);
        var validSig = Convert.ToBase64String(innerCheckpoint.Signature);
        var validLeaf = Convert.ToBase64String(CreateHash(1));
        // 65 sibling hashes – one more than the allowed maximum of 64.
        var tooManySiblings = string.Join(",", Enumerable.Repeat($"\"{validLeaf}\"", MerkleTree.MaxMerkleProofDepth + 1));
        var json = $"{{\"tick\":0,\"merkleRoot\":\"{validRoot}\",\"authorityPublicKey\":\"{validKey}\"," +
                   $"\"signature\":\"{validSig}\"," +
                   $"\"proof\":{{\"eventIndex\":0,\"leafHash\":\"{validLeaf}\",\"siblingHashes\":[{tooManySiblings}]}}}}";

        Assert.Throws<JsonException>(() =>
            MerkleCheckpoint.FromJsonBytes(System.Text.Encoding.UTF8.GetBytes(json)));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. SessionAuthority.VerifyMerkleCheckpoint with expectedMerkleRoot
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyMerkleCheckpoint_WithValidProof_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 1);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(1024, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, proof);

        // Verify with the correct expectedRoot – proof must be verified.
        Assert.True(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, root));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_WithTamperedProofLeaf_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 0);

        // Tamper the leaf hash in the proof.
        var tamperedLeaf = TamperByte(proof.LeafHash);
        var tamperedProof = new MerkleCheckpointProof(proof.EventIndex, tamperedLeaf, proof.SiblingHashes);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(2048, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, tamperedProof);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, root));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_WithProofAndNullExpectedRoot_SkipsProofCheck()
    {
        // When expectedMerkleRoot is null the proof is not verified even if present.
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 2);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(3072, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, proof);

        // Proof verification is skipped (no expectedMerkleRoot) – should still pass auth checks.
        Assert.True(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, expectedMerkleRoot: null));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_WithWrongExpectedRoot_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.BuildCheckpointProof(leaves, 3);
        var innerCheckpoint = authority.CreateMerkleCheckpoint(4096, root);
        var checkpoint = new MerkleCheckpoint(
            innerCheckpoint.Tick, innerCheckpoint.MerkleRoot,
            innerCheckpoint.AuthorityPublicKey, innerCheckpoint.Signature, proof);

        var wrongRoot = TamperByte(root);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, wrongRoot));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_NoProof_WithExpectedRoot_ReturnsTrue()
    {
        // Checkpoint has no proof but an expectedRoot is supplied – proof check is skipped.
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var root = CreateHash(99);
        var checkpoint = authority.CreateMerkleCheckpoint(5120, root);

        Assert.True(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, expectedMerkleRoot: root));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_MerkleRootMismatch_ReturnsFalse()
    {
        // When expectedMerkleRoot is supplied, checkpoint.MerkleRoot must equal it.
        // A valid signature over a different MerkleRoot must not be accepted.
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var root = CreateHash(50);
        var checkpoint = authority.CreateMerkleCheckpoint(6144, root);
        var differentRoot = TamperByte(root);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry, expectedMerkleRoot: differentRoot));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. TickAuthorityService.BuildCheckpointProof integration
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TickAuthorityService_BuildCheckpointProof_VerifiesAgainstTickMerkleRoot()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(7);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 5);
        var result = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(1), ticks);

        Assert.NotNull(result.TickMerkleRoot);
        var tickMerkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        // Generate proofs for every leaf index and verify each against TickMerkleRoot.
        for (var i = 0; i < ticks.Count; i++)
        {
            var proof = TickAuthorityService.BuildCheckpointProof(ticks, i);
            Assert.True(MerkleCheckpointProof.VerifyCheckpointProof(proof, tickMerkleRoot),
                $"Proof for tick chain index {i} must verify against TickMerkleRoot.");
        }
    }

    [Fact]
    public void TickAuthorityService_BuildCheckpointProof_TamperedLeaf_FailsVerification()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(8);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 4);
        var result = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(2), ticks);

        var tickMerkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        var proof = TickAuthorityService.BuildCheckpointProof(ticks, 0);

        var tamperedProof = new MerkleCheckpointProof(
            proof.EventIndex,
            TamperByte(proof.LeafHash),
            proof.SiblingHashes);

        Assert.False(MerkleCheckpointProof.VerifyCheckpointProof(tamperedProof, tickMerkleRoot));
    }

    [Fact]
    public void TickAuthorityService_BuildCheckpointProof_NullChain_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TickAuthorityService.BuildCheckpointProof(null!, 0));
    }

    [Fact]
    public void TickAuthorityService_BuildCheckpointProof_NullEntry_ThrowsArgumentException()
    {
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(10);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);
        var ticksWithNull = new List<SignedTick?>(ticks) { null };

        var ex = Assert.Throws<ArgumentException>(() =>
            TickAuthorityService.BuildCheckpointProof(ticksWithNull!, 0));
        Assert.Contains("null at index 3", ex.Message);
    }

    [Fact]
    public void TickAuthorityService_BuildCheckpointProof_IndexOutOfRange_Throws()
    {
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(9);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TickAuthorityService.BuildCheckpointProof(ticks, 3));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xAB;
        return hash;
    }

    private static byte[] TamperByte(byte[] input)
    {
        var copy = (byte[])input.Clone();
        copy[0] ^= 0xFF;
        return copy;
    }

    private static List<byte[]> BuildLeaves(int count)
    {
        var leaves = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var leaf = new byte[32];
            leaf[0] = (byte)(i & 0xFF);
            leaf[1] = (byte)((i >> 8) & 0xFF);
            leaf[16] = 0xCD;
            leaves.Add(leaf);
        }

        return leaves;
    }

    private static SessionAuthority CreateAuthority(string id = "checkpoint-proof-test")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static List<SignedTick> BuildSignedChain(
        SessionAuthority authority,
        SimulationState state,
        long startTick,
        int count)
    {
        var hasher = new StateHasher();
        var ticks = new List<SignedTick>(count);
        var prev = TickAuthorityService.GenesisStateHash;

        for (var i = 0; i < count; i++)
        {
            var tick = startTick + i * TickAuthorityService.SignInterval;
            var stateHash = hasher.HashTick(tick, state);
            var inputHash = new TickInputs(tick, [new PlayerInput(TestPlayerId, new ExfilAction())]).ComputeHash();
            var sig = authority.SignTick(tick, prev, stateHash, inputHash);
            ticks.Add(new SignedTick(tick, prev, stateHash, inputHash, sig));
            prev = stateHash;
        }

        return ticks;
    }
}
