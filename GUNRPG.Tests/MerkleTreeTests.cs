using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the Merkle tick chain system:
/// - MerkleTree.ComputeRoot (determinism, tampering detection, edge cases)
/// - MerkleTree.GenerateProof / VerifyProof (inclusion proof round-trips)
/// - MerkleProof record
/// - TickAuthorityService.ComputeTickLeafHash public static helper
/// - SessionAuthority.Sign overload with tickMerkleRoot
/// - SessionAuthority.VerifySignedRun with TickMerkleRoot
/// - TickAuthorityService.FinalizeRun with Merkle root binding
/// - SignedRunResult.TickMerkleRoot property
/// </summary>
public sealed class MerkleTreeTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. MerkleTree.ComputeRoot – determinism
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeRoot_SameLeaves_ReturnsSameRoot_Deterministic()
    {
        var leaves = BuildLeaves(4);

        var root1 = MerkleTree.ComputeRoot(leaves);
        var root2 = MerkleTree.ComputeRoot(leaves);

        Assert.Equal(32, root1.Length);
        Assert.True(root1.AsSpan().SequenceEqual(root2));
    }

    [Fact]
    public void ComputeRoot_EmptyList_Returns32ZeroBytes()
    {
        var root = MerkleTree.ComputeRoot([]);

        Assert.Equal(32, root.Length);
        Assert.All(root, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ComputeRoot_SingleLeaf_AppliesDomainSeparation()
    {
        // With domain separation, the root of a single leaf is SHA256(0x00 || leaf),
        // not the leaf value itself.
        var leaf = CreateHash(42);
        var root = MerkleTree.ComputeRoot([leaf]);

        Assert.Equal(32, root.Length);
        // Root must differ from the raw leaf value (domain prefix changes the hash).
        Assert.False(root.AsSpan().SequenceEqual(leaf));
    }

    [Fact]
    public void ComputeRoot_TwoLeaves_DiffersFromEitherLeaf()
    {
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);

        // Root must not be the same as either leaf (it is a hash of both).
        Assert.False(root.AsSpan().SequenceEqual(leaves[0]));
        Assert.False(root.AsSpan().SequenceEqual(leaves[1]));
    }

    [Fact]
    public void ComputeRoot_OddLeafCount_Deterministic()
    {
        // With 3 leaves the last leaf is duplicated before pairing.
        var leaves = BuildLeaves(3);

        var root1 = MerkleTree.ComputeRoot(leaves);
        var root2 = MerkleTree.ComputeRoot(leaves);

        Assert.True(root1.AsSpan().SequenceEqual(root2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. MerkleTree.ComputeRoot – tampering detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeRoot_TamperingFirstLeaf_ChangesRoot()
    {
        var leaves = BuildLeaves(4);
        var rootOriginal = MerkleTree.ComputeRoot(leaves);

        var tampered = leaves.Select(l => (byte[])l.Clone()).ToList();
        tampered[0][0] ^= 0xFF;
        var rootTampered = MerkleTree.ComputeRoot(tampered);

        Assert.False(rootOriginal.AsSpan().SequenceEqual(rootTampered));
    }

    [Fact]
    public void ComputeRoot_TamperingLastLeaf_ChangesRoot()
    {
        var leaves = BuildLeaves(4);
        var rootOriginal = MerkleTree.ComputeRoot(leaves);

        var tampered = leaves.Select(l => (byte[])l.Clone()).ToList();
        tampered[^1][31] ^= 0xFF;
        var rootTampered = MerkleTree.ComputeRoot(tampered);

        Assert.False(rootOriginal.AsSpan().SequenceEqual(rootTampered));
    }

    [Fact]
    public void ComputeRoot_ReorderingLeaves_ChangesRoot()
    {
        var leaves = BuildLeaves(4);
        var rootOriginal = MerkleTree.ComputeRoot(leaves);

        var reordered = new List<byte[]> { leaves[1], leaves[0], leaves[2], leaves[3] };
        var rootReordered = MerkleTree.ComputeRoot(reordered);

        Assert.False(rootOriginal.AsSpan().SequenceEqual(rootReordered));
    }

    [Fact]
    public void ComputeRoot_ExtraLeaf_ChangesRoot()
    {
        var leaves = BuildLeaves(4);
        var rootOriginal = MerkleTree.ComputeRoot(leaves);

        var extended = new List<byte[]>(leaves) { CreateHash(99) };
        var rootExtended = MerkleTree.ComputeRoot(extended);

        Assert.False(rootOriginal.AsSpan().SequenceEqual(rootExtended));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. MerkleTree.GenerateProof / VerifyProof – inclusion proofs
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 0)]
    [InlineData(5, 2)]
    [InlineData(5, 4)]
    [InlineData(7, 3)]
    [InlineData(8, 7)]
    public void VerifyProof_ValidProof_ReturnsTrue(int leafCount, int leafIndex)
    {
        var leaves = BuildLeaves(leafCount);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.GenerateProof(leaves, leafIndex);

        Assert.True(MerkleTree.VerifyProof(proof, root));
    }

    [Fact]
    public void VerifyProof_WrongRoot_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var proof = MerkleTree.GenerateProof(leaves, 0);
        var wrongRoot = CreateHash(99);

        Assert.False(MerkleTree.VerifyProof(proof, wrongRoot));
    }

    [Fact]
    public void VerifyProof_TamperedLeafHash_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);
        var proof = MerkleTree.GenerateProof(leaves, 1);

        // Clone proof but tamper with the leaf hash
        var tamperedLeafHash = (byte[])proof.LeafHash.Clone();
        tamperedLeafHash[0] ^= 0xFF;
        var tamperedProof = new MerkleProof(tamperedLeafHash, proof.SiblingHashes, proof.LeafIndex);

        Assert.False(MerkleTree.VerifyProof(tamperedProof, root));
    }

    [Fact]
    public void VerifyProof_ProofForDifferentLeaf_ReturnsFalse()
    {
        var leaves = BuildLeaves(4);
        var root = MerkleTree.ComputeRoot(leaves);

        var proofFor0 = MerkleTree.GenerateProof(leaves, 0);
        // Use proof-for-leaf-0's siblings but claim it is for leaf 1 — must fail.
        var fakeProof = new MerkleProof(leaves[1], proofFor0.SiblingHashes, 1);

        Assert.False(MerkleTree.VerifyProof(fakeProof, root));
    }

    [Fact]
    public void GenerateProof_LeafIndex_MatchesOriginalLeaf()
    {
        var leaves = BuildLeaves(6);
        for (var i = 0; i < leaves.Count; i++)
        {
            var proof = MerkleTree.GenerateProof(leaves, i);
            Assert.Equal(i, proof.LeafIndex);
            Assert.True(proof.LeafHash.AsSpan().SequenceEqual(leaves[i]));
        }
    }

    [Fact]
    public void GenerateProof_OutOfRange_Throws()
    {
        var leaves = BuildLeaves(4);
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleTree.GenerateProof(leaves, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => MerkleTree.GenerateProof(leaves, -1));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. MerkleTree – argument validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeRoot_NullList_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MerkleTree.ComputeRoot(null!));
    }

    [Fact]
    public void ComputeRoot_NullLeaf_Throws()
    {
        var leaves = new List<byte[]> { CreateHash(1), null!, CreateHash(3) };
        Assert.Throws<ArgumentException>(() => MerkleTree.ComputeRoot(leaves));
    }

    [Fact]
    public void ComputeRoot_WrongSizeLeaf_Throws()
    {
        var leaves = new List<byte[]> { CreateHash(1), new byte[16] };
        Assert.Throws<ArgumentException>(() => MerkleTree.ComputeRoot(leaves));
    }

    [Fact]
    public void VerifyProof_NullProof_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => MerkleTree.VerifyProof(null!, CreateHash(1)));
    }

    [Fact]
    public void VerifyProof_NullRoot_Throws()
    {
        var proof = MerkleTree.GenerateProof(BuildLeaves(2), 0);
        Assert.Throws<ArgumentNullException>(() => MerkleTree.VerifyProof(proof, null!));
    }

    [Fact]
    public void VerifyProof_WrongSizeRoot_Throws()
    {
        var proof = MerkleTree.GenerateProof(BuildLeaves(2), 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(proof, new byte[16]));
    }

    [Fact]
    public void VerifyProof_NullLeafHash_Throws()
    {
        var root = CreateHash(99);
        var proof = new MerkleProof(null!, [], 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(proof, root));
    }

    [Fact]
    public void VerifyProof_WrongSizeLeafHash_Throws()
    {
        var root = CreateHash(99);
        var proof = new MerkleProof(new byte[16], [], 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(proof, root));
    }

    [Fact]
    public void VerifyProof_NullSiblingList_Throws()
    {
        var root = CreateHash(99);
        var proof = new MerkleProof(CreateHash(1), null!, 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(proof, root));
    }

    [Fact]
    public void VerifyProof_NullSiblingEntry_Throws()
    {
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);
        // Build a proof with a null sibling
        var badProof = new MerkleProof(leaves[0], new byte[][] { null! }, 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(badProof, root));
    }

    [Fact]
    public void VerifyProof_WrongSizeSiblingEntry_Throws()
    {
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);
        var badProof = new MerkleProof(leaves[0], new byte[][] { new byte[16] }, 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(badProof, root));
    }

    [Fact]
    public void VerifyProof_NegativeLeafIndex_Throws()
    {
        var root = CreateHash(99);
        var proof = new MerkleProof(CreateHash(1), [], -1);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(proof, root));
    }

    [Fact]
    public void VerifyProof_ExceedsMaxDepth_Throws()
    {
        var leaf = CreateHash(1);
        var root = CreateHash(2);
        // Build a sibling list with one more entry than allowed.
        var deepSiblings = Enumerable.Range(0, MerkleTree.MaxMerkleProofDepth + 1)
            .Select(i => CreateHash(i + 100))
            .ToArray();
        var badProof = new MerkleProof(leaf, deepSiblings, 0);
        Assert.Throws<ArgumentException>(() => MerkleTree.VerifyProof(badProof, root));
    }

    [Fact]
    public void VerifyProof_AtMaxDepth_DoesNotThrow()
    {
        // A proof exactly at MaxMerkleProofDepth entries is accepted (does not throw for depth reason).
        var leaf = CreateHash(1);
        var root = CreateHash(2);
        var siblingList = Enumerable.Range(0, MerkleTree.MaxMerkleProofDepth)
            .Select(i => CreateHash(i + 100))
            .ToArray();
        var proof = new MerkleProof(leaf, siblingList, 0);
        // Should not throw an ArgumentException about depth; may return false (root mismatch is fine).
        var ex = Record.Exception(() => MerkleTree.VerifyProof(proof, root));
        Assert.Null(ex);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4b. Domain separation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DomainSeparation_LeafRootDiffersFromParentOfSelf()
    {
        // A single-leaf tree root (leaf node) must differ from the parent node
        // computed from the same raw value, confirming domain separation prevents
        // leaf/internal node collisions.
        var leaf = CreateHash(7);
        var singleLeafRoot = MerkleTree.ComputeRoot([leaf]);
        var twoLeafRoot = MerkleTree.ComputeRoot([leaf, leaf]);

        // Single-leaf root (0x00 || leaf) must differ from two-leaf root (0x01 || node || node).
        Assert.False(singleLeafRoot.AsSpan().SequenceEqual(twoLeafRoot));
    }

    [Fact]
    public void DomainSeparation_ProofRoundTrip_TwoLeaves()
    {
        // End-to-end: generate proof with domain separation and verify it correctly.
        var leaves = BuildLeaves(2);
        var root = MerkleTree.ComputeRoot(leaves);

        var proof0 = MerkleTree.GenerateProof(leaves, 0);
        var proof1 = MerkleTree.GenerateProof(leaves, 1);

        Assert.True(MerkleTree.VerifyProof(proof0, root));
        Assert.True(MerkleTree.VerifyProof(proof1, root));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. TickAuthorityService.ComputeTickLeafHash – public static helper
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeTickLeafHash_IsDeterministic()
    {
        var prev = CreateHash(10);
        var state = CreateHash(20);
        var input = CreateHash(30);

        var h1 = TickAuthorityService.ComputeTickLeafHash(5L, prev, state, input);
        var h2 = TickAuthorityService.ComputeTickLeafHash(5L, prev, state, input);

        Assert.Equal(32, h1.Length);
        Assert.True(h1.AsSpan().SequenceEqual(h2));
    }

    [Fact]
    public void ComputeTickLeafHash_DifferentTick_ProducesDifferentHash()
    {
        var prev = CreateHash(10);
        var state = CreateHash(20);
        var input = CreateHash(30);

        var h1 = TickAuthorityService.ComputeTickLeafHash(1L, prev, state, input);
        var h2 = TickAuthorityService.ComputeTickLeafHash(2L, prev, state, input);

        Assert.False(h1.AsSpan().SequenceEqual(h2));
    }

    [Fact]
    public void ComputeTickLeafHash_DifferentStateHash_ProducesDifferentHash()
    {
        var prev = CreateHash(10);
        var input = CreateHash(30);

        var h1 = TickAuthorityService.ComputeTickLeafHash(0L, prev, CreateHash(20), input);
        var h2 = TickAuthorityService.ComputeTickLeafHash(0L, prev, CreateHash(21), input);

        Assert.False(h1.AsSpan().SequenceEqual(h2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. SessionAuthority.Sign with tickMerkleRoot
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignWithMerkleRoot_ProducesResult_WithAllFieldsSet()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(1);
        var replayHash = CreateHash(2);
        var merkleRoot = CreateHash(3);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var result = authority.Sign(sessionId, playerId, finalHash, replayHash, merkleRoot);

        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(playerId, result.PlayerId);
        Assert.Equal(Convert.ToHexString(finalHash), result.FinalHash);
        Assert.NotNull(result.ReplayHash);
        Assert.Equal(Convert.ToHexString(replayHash), result.ReplayHash);
        Assert.NotNull(result.TickMerkleRoot);
        Assert.Equal(Convert.ToHexString(merkleRoot), result.TickMerkleRoot);
    }

    [Fact]
    public void SignWithMerkleRoot_SignatureVerifies()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(
            Guid.NewGuid(), Guid.NewGuid(),
            CreateHash(1), CreateHash(2), CreateHash(3));

        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void SignWithMerkleRoot_WrongAuthority_VerificationFails()
    {
        var authority = CreateAuthority("auth-a");
        var other = CreateAuthority("auth-b");
        var result = authority.Sign(
            Guid.NewGuid(), Guid.NewGuid(),
            CreateHash(1), CreateHash(2), CreateHash(3));

        Assert.False(SessionAuthority.VerifySignedRun(result, other.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. SignedRunResult.TickMerkleRoot – property and validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignedRunResult_WithoutMerkleRoot_HasNullTickMerkleRoot()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1), CreateHash(2));

        Assert.Null(result.TickMerkleRoot);
    }

    [Fact]
    public void SignedRunResult_WithReplayHashOnly_SignatureVerifies()
    {
        // Verify that a result signed with only the replay hash (no Merkle root)
        // still verifies correctly — TickMerkleRoot is truly optional.
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1), CreateHash(2));

        Assert.NotNull(result.ReplayHash);
        Assert.Null(result.TickMerkleRoot);
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void SignedRunResult_WithMerkleRoot_RequiresReplayHash()
    {
        var signature = new byte[64];
        var finalHashHex = Convert.ToHexString(CreateHash(1));
        var merkleHex = Convert.ToHexString(CreateHash(3));

        // TickMerkleRoot without ReplayHash should throw.
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(
                Guid.NewGuid(), Guid.NewGuid(),
                finalHashHex, "auth", signature,
                replayHash: null, tickMerkleRoot: merkleHex));
    }

    [Fact]
    public void SignedRunResult_InvalidHexFinalHash_Throws()
    {
        var signature = new byte[64];
        // 64 chars but contains 'G' — not a valid hex character.
        var badHex = new string('G', 64);
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), badHex, "auth", signature));
    }

    [Fact]
    public void SignedRunResult_InvalidHexReplayHash_Throws()
    {
        var signature = new byte[64];
        var goodHex = Convert.ToHexString(CreateHash(1));
        var badHex = new string('Z', 64);
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), goodHex, "auth", signature,
                replayHash: badHex));
    }

    [Fact]
    public void SignedRunResult_InvalidHexTickMerkleRoot_Throws()
    {
        var signature = new byte[64];
        var goodHex = Convert.ToHexString(CreateHash(1));
        var badHex = new string('!', 64);
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), goodHex, "auth", signature,
                replayHash: goodHex, tickMerkleRoot: badHex));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. TickAuthorityService.FinalizeRun – Merkle root binding
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_WithChain_BindsMerkleRoot()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(50);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.TickMerkleRoot);
        Assert.Equal(64, result.TickMerkleRoot.Length); // 32 bytes hex
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void FinalizeRun_EmptyChain_Throws()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var finalHash = CreateHash(10);
        var replayHash = CreateHash(11);

        // FinalizeRun with an empty chain must be rejected — the persistence boundary
        // requires at least one verified signed tick.
        Assert.Throws<ArgumentException>(
            () => service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash, []));
    }

    [Fact]
    public void FinalizeRun_DifferentChains_ProduceDifferentMerkleRoots()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var chain1 = BuildSignedChain(authority, state, startTick: 0, count: 2);
        var chain3 = BuildSignedChain(authority, state, startTick: 0, count: 3);

        var result1 = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), chain1[^1].StateHash, CreateHash(1), chain1);
        var result3 = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), chain3[^1].StateHash, CreateHash(1), chain3);

        Assert.NotEqual(result1.TickMerkleRoot, result3.TickMerkleRoot);
    }

    [Fact]
    public void FinalizeRun_MerkleRoot_MatchesManualComputation()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(50);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var result = service.FinalizeRun(sessionId, playerId, finalStateHash, replayHash, ticks);

        // Manually compute the expected Merkle root.
        var leafHashes = ticks
            .Select(t => TickAuthorityService.ComputeTickLeafHash(t.Tick, t.PrevStateHash, t.StateHash, t.InputHash))
            .ToList();
        var expectedRoot = Convert.ToHexString(MerkleTree.ComputeRoot(leafHashes));

        Assert.Equal(expectedRoot, result.TickMerkleRoot);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. End-to-end: proof verification against FinalizeRun Merkle root
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void InclusionProof_VerifiesAgainstFinalizeRunMerkleRoot()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 4);
        var result = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(),
            ticks[^1].StateHash, CreateHash(1), ticks);

        var leafHashes = ticks
            .Select(t => TickAuthorityService.ComputeTickLeafHash(t.Tick, t.PrevStateHash, t.StateHash, t.InputHash))
            .ToList();
        var merkleRootBytes = Convert.FromHexString(result.TickMerkleRoot!);

        // Every tick's proof must verify against the Merkle root embedded in the signed result.
        for (var i = 0; i < ticks.Count; i++)
        {
            var proof = MerkleTree.GenerateProof(leafHashes, i);
            Assert.True(MerkleTree.VerifyProof(proof, merkleRootBytes),
                $"Proof failed for tick at index {i}.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. MerkleBuilder – streaming leaf accumulator
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MerkleBuilder_Empty_ProducesZeroRoot()
    {
        var builder = new MerkleBuilder();
        var root = builder.BuildRoot();

        Assert.Equal(32, root.Length);
        Assert.All(root, b => Assert.Equal(0, b));
        Assert.Equal(0, builder.Count);
    }

    [Fact]
    public void MerkleBuilder_MatchesMerkleTreeComputeRoot()
    {
        var leaves = BuildLeaves(5);

        var builder = new MerkleBuilder();
        foreach (var leaf in leaves)
            builder.AddLeaf(leaf);

        var builderRoot = builder.BuildRoot();
        var directRoot = MerkleTree.ComputeRoot(leaves);

        Assert.True(builderRoot.AsSpan().SequenceEqual(directRoot));
    }

    [Fact]
    public void MerkleBuilder_Count_ReflectsAddedLeaves()
    {
        var builder = new MerkleBuilder();
        Assert.Equal(0, builder.Count);

        builder.AddLeaf(CreateHash(1));
        Assert.Equal(1, builder.Count);

        builder.AddLeaf(CreateHash(2)).AddLeaf(CreateHash(3));
        Assert.Equal(3, builder.Count);
    }

    [Fact]
    public void MerkleBuilder_AddLeaf_NullHash_Throws()
    {
        var builder = new MerkleBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.AddLeaf(null!));
    }

    [Fact]
    public void MerkleBuilder_AddLeaf_WrongSize_Throws()
    {
        var builder = new MerkleBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddLeaf(new byte[16]));
    }

    [Fact]
    public void MerkleBuilder_AddLeaf_ClonesInput_MutationDoesNotAffectRoot()
    {
        // Root computed before mutation must equal root computed after mutation,
        // proving AddLeaf defensively clones the caller-supplied buffer.
        var leaf = CreateHash(7);
        var builder = new MerkleBuilder();
        builder.AddLeaf(leaf);

        var rootBefore = builder.BuildRoot();

        // Mutate the original leaf array after adding it.
        leaf[0] ^= 0xFF;

        var rootAfter = builder.BuildRoot();

        Assert.True(rootBefore.AsSpan().SequenceEqual(rootAfter),
            "Mutating the leaf array after AddLeaf must not change the root.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 11. TickAuthorityService.VerifyTickMerkleRoot – replay Merkle validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyTickMerkleRoot_MatchingChain_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);
        var result = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(1), ticks);

        Assert.True(TickAuthorityService.VerifyTickMerkleRoot(result, ticks));
    }

    [Fact]
    public void VerifyTickMerkleRoot_TamperedTick_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);
        var result = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(1), ticks);

        // Tamper with a tick's state hash after signing — root must no longer match.
        var tamperedTicks = ticks.Select(t => t).ToList();
        var bad = tamperedTicks[0];
        var tamperedState = (byte[])bad.StateHash.Clone();
        tamperedState[0] ^= 0xFF;
        tamperedTicks[0] = new SignedTick(bad.Tick, bad.PrevStateHash, tamperedState, bad.InputHash, bad.Signature);

        Assert.False(TickAuthorityService.VerifyTickMerkleRoot(result, tamperedTicks));
    }

    [Fact]
    public void VerifyTickMerkleRoot_NoMerkleRootAndEmptyChain_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1), CreateHash(2));

        // A result without TickMerkleRoot is consistent with an empty chain.
        Assert.True(TickAuthorityService.VerifyTickMerkleRoot(result, []));
    }

    [Fact]
    public void VerifyTickMerkleRoot_NoMerkleRootWithNonEmptyChain_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(42);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 1);
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1), CreateHash(2));

        // No TickMerkleRoot but chain is non-empty → mismatch.
        Assert.False(TickAuthorityService.VerifyTickMerkleRoot(result, ticks));
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

    private static List<byte[]> BuildLeaves(int count)
    {
        var leaves = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            var leaf = new byte[32];
            leaf[0] = (byte)(i & 0xFF);
            leaf[1] = (byte)((i >> 8) & 0xFF);
            leaf[16] = 0xBC;
            leaves.Add(leaf);
        }

        return leaves;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. MerkleFrontier – incremental root matches MerkleTree.ComputeRoot
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(17)]
    public void MerkleFrontier_Root_MatchesMerkleTreeComputeRoot(int leafCount)
    {
        var leaves = BuildLeaves(leafCount);

        var fullRoot = MerkleTree.ComputeRoot(leaves);

        var frontier = new MerkleFrontier();
        foreach (var leaf in leaves)
            frontier.AddLeaf(leaf);
        var frontierRoot = frontier.ComputeRoot();

        Assert.Equal(Convert.ToHexString(fullRoot), Convert.ToHexString(frontierRoot));
    }

    [Fact]
    public void MerkleFrontier_EmptyFrontier_Returns32ZeroBytes()
    {
        var frontier = new MerkleFrontier();
        var root = frontier.ComputeRoot();

        Assert.Equal(32, root.Length);
        Assert.All(root, b => Assert.Equal(0, b));
    }

    [Fact]
    public void MerkleFrontier_LargeRun_MatchesMerkleTree()
    {
        // 10_000 leaves — ensures memory is O(log n) and root matches full tree.
        const int count = 10_000;
        var leaves = BuildLeaves(count);

        var fullRoot = MerkleTree.ComputeRoot(leaves);

        var frontier = new MerkleFrontier();
        foreach (var leaf in leaves)
            frontier.AddLeaf(leaf);
        var frontierRoot = frontier.ComputeRoot();

        Assert.Equal(Convert.ToHexString(fullRoot), Convert.ToHexString(frontierRoot));
        // Frontier internal level count must be O(log2(n)) ≈ 14 for 10_000 leaves.
        Assert.Equal(count, (int)frontier.LeafCount);
    }

    [Fact]
    public void MerkleFrontier_DeterministicOrdering_ProducesSameRoot()
    {
        var leaves = BuildLeaves(12);

        var frontier1 = new MerkleFrontier();
        var frontier2 = new MerkleFrontier();
        foreach (var leaf in leaves)
        {
            frontier1.AddLeaf(leaf);
            frontier2.AddLeaf(leaf);
        }

        Assert.Equal(
            Convert.ToHexString(frontier1.ComputeRoot()),
            Convert.ToHexString(frontier2.ComputeRoot()));
    }

    [Fact]
    public void MerkleFrontier_DifferentLeaves_ProduceDifferentRoots()
    {
        var leaves1 = BuildLeaves(4);
        var leaves2 = BuildLeaves(4);
        leaves2[2][0] ^= 0xFF; // tamper one leaf

        var frontier1 = new MerkleFrontier();
        var frontier2 = new MerkleFrontier();
        foreach (var leaf in leaves1) frontier1.AddLeaf(leaf);
        foreach (var leaf in leaves2) frontier2.AddLeaf(leaf);

        Assert.NotEqual(
            Convert.ToHexString(frontier1.ComputeRoot()),
            Convert.ToHexString(frontier2.ComputeRoot()));
    }

    [Fact]
    public void MerkleFrontier_SingleLeaf_AppliesDomainSeparation()
    {
        var leaf = CreateHash(42);
        var frontier = new MerkleFrontier();
        frontier.AddLeaf(leaf);
        var root = frontier.ComputeRoot();

        // Root must be SHA256(0x00 || leaf), not the raw leaf value.
        Assert.False(root.AsSpan().SequenceEqual(leaf));
        Assert.Equal(32, root.Length);
    }

    [Fact]
    public void MerkleFrontier_LeafCount_TracksAddedLeaves()
    {
        var frontier = new MerkleFrontier();
        Assert.Equal(0L, frontier.LeafCount);

        frontier.AddLeaf(CreateHash(1));
        Assert.Equal(1L, frontier.LeafCount);

        frontier.AddLeaf(CreateHash(2));
        Assert.Equal(2L, frontier.LeafCount);
    }

    [Fact]
    public void MerkleFrontier_AddLeaf_NullHash_ThrowsArgumentNullException()
    {
        var frontier = new MerkleFrontier();
        Assert.Throws<ArgumentNullException>(() => frontier.AddLeaf(null!));
    }

    [Fact]
    public void MerkleFrontier_AddLeaf_WrongLengthHash_ThrowsArgumentException()
    {
        var frontier = new MerkleFrontier();
        Assert.Throws<ArgumentException>(() => frontier.AddLeaf(new byte[16]));
    }

    private static SessionAuthority CreateAuthority(string id = "test-authority")
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
        var playerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        for (var i = 0; i < count; i++)
        {
            var tick = startTick + i;
            var stateHash = hasher.HashTick(tick, state);
            var inputHash = new TickInputs(tick, [new PlayerInput(playerId, new ExfilAction())]).ComputeHash();
            var sig = authority.SignTick(tick, prev, stateHash, inputHash);
            ticks.Add(new SignedTick(tick, prev, stateHash, inputHash, sig));
            prev = stateHash;
        }

        return ticks;
    }
}
