using System.Security.Cryptography;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for Merkle Proof-of-Divergence:
/// - <see cref="DivergenceProof"/> model safety requirements
/// - <see cref="ReplayVerifier.CreateDivergenceProof"/> construction
/// - <see cref="ReplayVerifier.VerifyDivergenceProof"/> acceptance and rejection
/// - <see cref="ReplayVerifier.TryVerifyRun"/> divergence detection and proof generation
/// - Third-party verification (node A generates, node B verifies without replay)
/// </summary>
public sealed class DivergenceProofTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. DivergenceProof model – structural validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DivergenceProof_ValidFields_IsStructurallyValid()
    {
        var proof = new DivergenceProof(
            TickIndex: 0,
            LeafIndex: 0,
            ExpectedTickHash: CreateHash(1),
            ActualTickHash: CreateHash(2),
            MerkleProof: []);

        Assert.True(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_NullExpectedHash_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, 0, null!, CreateHash(2), []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_WrongLengthExpectedHash_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, 0, new byte[16], CreateHash(2), []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_NullActualHash_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, 0, CreateHash(1), null!, []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_WrongLengthActualHash_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, 0, CreateHash(1), new byte[16], []);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_NullMerkleProof_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, 0, CreateHash(1), CreateHash(2), null!);
        Assert.False(proof.IsStructurallyValid);
    }

    [Fact]
    public void DivergenceProof_NegativeLeafIndex_NotStructurallyValid()
    {
        var proof = new DivergenceProof(0, -1, CreateHash(1), CreateHash(2), []);
        Assert.False(proof.IsStructurallyValid);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. CreateDivergenceProof – construction
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDivergenceProof_ValidTick_ProducesNonNullProof()
    {
        var (authority, service, ticks, result) = BuildValidRun(seed: 1, tickPositions: [0, 256, 512]);

        var divergentTick = ticks[1];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);
        var actualHash = CreateHash(99);

        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, actualHash);

        Assert.NotNull(proof);
        Assert.Equal(divergentTick.Tick, proof.TickIndex);
        Assert.Equal(1, proof.LeafIndex);
        Assert.Equal(32, proof.ExpectedTickHash.Length);
        Assert.Equal(32, proof.ActualTickHash.Length);
        Assert.NotNull(proof.MerkleProof);
    }

    [Fact]
    public void CreateDivergenceProof_TickNotInChain_Throws()
    {
        var (_, _, ticks, _) = BuildValidRun(seed: 2, tickPositions: [0, 256, 512]);

        Assert.Throws<ArgumentException>(() =>
            ReplayVerifier.CreateDivergenceProof(ticks, 9999L, CreateHash(1), CreateHash(2)));
    }

    [Fact]
    public void CreateDivergenceProof_NullTickChain_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReplayVerifier.CreateDivergenceProof(null!, 0, CreateHash(1), CreateHash(2)));
    }

    [Fact]
    public void CreateDivergenceProof_NullExpectedHash_Throws()
    {
        var (_, _, ticks, _) = BuildValidRun(seed: 3, tickPositions: [0, 256]);
        Assert.Throws<ArgumentNullException>(() =>
            ReplayVerifier.CreateDivergenceProof(ticks, 0, null!, CreateHash(2)));
    }

    [Fact]
    public void CreateDivergenceProof_NullActualHash_Throws()
    {
        var (_, _, ticks, _) = BuildValidRun(seed: 4, tickPositions: [0, 256]);
        Assert.Throws<ArgumentNullException>(() =>
            ReplayVerifier.CreateDivergenceProof(ticks, 0, CreateHash(1), null!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. VerifyDivergenceProof – valid proof
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyDivergenceProof_ValidProof_ReturnsTrue()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 10, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var divergentTick = ticks[1];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);
        // Actual hash differs from expected (simulating divergence)
        var actualHash = CreateHash(42);

        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, actualHash);

        Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot));
    }

    [Fact]
    public void VerifyDivergenceProof_ValidProof_AllTicks_VerifyCorrectly()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 11, tickPositions: [0, 256, 512, 768]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        for (var i = 0; i < ticks.Count; i++)
        {
            var t = ticks[i];
            var expectedHash = TickAuthorityService.ComputeTickLeafHash(
                t.Tick, t.PrevStateHash, t.StateHash, t.InputHash);
            var actualHash = CreateHash(i + 100);

            var proof = ReplayVerifier.CreateDivergenceProof(ticks, t.Tick, expectedHash, actualHash);
            Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot),
                $"Proof for tick at index {i} should verify.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. VerifyDivergenceProof – tampered proof (rejection)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyDivergenceProof_ModifiedSiblingHash_ReturnsFalse()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 20, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var divergentTick = ticks[1];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);

        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, CreateHash(99));

        // Tamper with a sibling hash
        var tamperedSiblings = proof.MerkleProof
            .Select((s, i) => i == 0 ? TamperHash(s) : (byte[])s.Clone())
            .ToList();
        var tamperedProof = proof with { MerkleProof = tamperedSiblings };

        Assert.False(ReplayVerifier.VerifyDivergenceProof(tamperedProof, merkleRoot));
    }

    [Fact]
    public void VerifyDivergenceProof_ModifiedExpectedTickHash_ReturnsFalse()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 21, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var divergentTick = ticks[1];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);

        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, CreateHash(99));

        // Tamper with the expected tick hash (should break Merkle inclusion)
        var tamperedProof = proof with { ExpectedTickHash = TamperHash(proof.ExpectedTickHash) };

        Assert.False(ReplayVerifier.VerifyDivergenceProof(tamperedProof, merkleRoot));
    }

    [Fact]
    public void VerifyDivergenceProof_WrongMerkleRoot_ReturnsFalse()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 22, tickPositions: [0, 256, 512]);
        var wrongRoot = CreateHash(77); // random wrong root

        var divergentTick = ticks[0];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);

        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, CreateHash(99));

        Assert.False(ReplayVerifier.VerifyDivergenceProof(proof, wrongRoot));
    }

    [Fact]
    public void VerifyDivergenceProof_ExpectedEqualsActual_ReturnsFalse()
    {
        // A proof where expected == actual does not demonstrate divergence.
        var (_, _, ticks, result) = BuildValidRun(seed: 23, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var divergentTick = ticks[0];
        var expectedHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);

        // Actual hash is the same as expected (no divergence)
        var proof = ReplayVerifier.CreateDivergenceProof(ticks, divergentTick.Tick, expectedHash, (byte[])expectedHash.Clone());

        Assert.False(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot));
    }

    [Fact]
    public void VerifyDivergenceProof_NullProof_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReplayVerifier.VerifyDivergenceProof(null!, CreateHash(1)));
    }

    [Fact]
    public void VerifyDivergenceProof_NullMerkleRoot_Throws()
    {
        var proof = new DivergenceProof(0, 0, CreateHash(1), CreateHash(2), []);
        Assert.Throws<ArgumentNullException>(() =>
            ReplayVerifier.VerifyDivergenceProof(proof, null!));
    }

    [Fact]
    public void VerifyDivergenceProof_StructurallyInvalidProof_ReturnsFalse()
    {
        // Null MerkleProof field → structurally invalid → returns false rather than throwing.
        var proof = new DivergenceProof(0, 0, CreateHash(1), CreateHash(2), null!);
        var merkleRoot = CreateHash(3);

        Assert.False(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. TryVerifyRun – valid run
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryVerifyRun_ValidRun_ReturnsTrueNullProof()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 30, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(30));

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.True(valid);
        Assert.Null(proof);
    }

    [Fact]
    public void TryVerifyRun_ValidRun_ManyCheckpoints_ReturnsTrueNullProof()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 31, tickPositions: [0, 256, 512, 768, 1024]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(31));

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.True(valid);
        Assert.Null(proof);
    }

    [Fact]
    public void TryVerifyRun_ValidRun_NoCheckpoints_ReturnsTrueNullProof()
    {
        var (authority, _, ticks, result) = BuildValidRunNoCheckpoints(seed: 32, tickPositions: [0, 256]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(32));

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.True(valid);
        Assert.Null(proof);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. TryVerifyRun – divergence detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryVerifyRun_DivergingSimulation_ReturnsFalseWithProof()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 40, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(40));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[1].Tick);

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.False(valid);
        Assert.NotNull(proof);
    }

    [Fact]
    public void TryVerifyRun_DivergingSimulation_ProofIdentifiesCorrectTickIndex()
    {
        // Simulation diverges at tick 256 (index 1 in chain [0, 256, 512]).
        var (authority, _, ticks, result) = BuildValidRun(seed: 41, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(41));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[1].Tick);

        verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.NotNull(proof);
        // The divergent tick must be at or after tick 256 (the divergence point).
        Assert.True(proof.TickIndex >= ticks[1].Tick,
            $"Expected proof.TickIndex >= {ticks[1].Tick}, got {proof.TickIndex}");
    }

    [Fact]
    public void TryVerifyRun_EarlyDivergence_ProofVerifiesAgainstMerkleRoot()
    {
        // Diverge from the very first tick.
        var (authority, _, ticks, result) = BuildValidRun(seed: 42, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(42));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[0].Tick);

        verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.NotNull(proof);
        Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot),
            "Generated divergence proof must verify against the run's Merkle root.");
    }

    [Fact]
    public void TryVerifyRun_LateDivergence_ProofVerifiesAgainstMerkleRoot()
    {
        // Diverge at the last tick.
        var (authority, _, ticks, result) = BuildValidRun(seed: 43, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(43));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[^1].Tick);

        verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.NotNull(proof);
        Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot),
            "Generated divergence proof must verify against the run's Merkle root.");
    }

    [Fact]
    public void TryVerifyRun_MiddleDivergence_ProofVerifiesAgainstMerkleRoot()
    {
        // Diverge in the middle of a multi-checkpoint run.
        var (authority, _, ticks, result) = BuildValidRun(seed: 44, tickPositions: [0, 256, 512, 768, 1024]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(44));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[2].Tick);

        verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.NotNull(proof);
        Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot),
            "Generated divergence proof must verify against the run's Merkle root.");
    }

    [Fact]
    public void TryVerifyRun_NoCheckpoints_DivergingSimulation_ProofVerifies()
    {
        var (authority, _, ticks, result) = BuildValidRunNoCheckpoints(seed: 45, tickPositions: [0, 256]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(45));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: 0);

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.False(valid);
        Assert.NotNull(proof);
        Assert.True(ReplayVerifier.VerifyDivergenceProof(proof, merkleRoot));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. TryVerifyRun – non-divergence failures produce null proof
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryVerifyRun_WrongAuthority_ReturnsFalseNullProof()
    {
        var (_, _, ticks, result) = BuildValidRun(seed: 50, tickPositions: [0, 256, 512]);
        // Use a different authority for verification
        var wrongAuthority = CreateAuthority("wrong-authority");
        var verifier = new ReplayVerifier(wrongAuthority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(50));

        var valid = verifier.TryVerifyRun(ticks, result, simulation, out var proof);

        Assert.False(valid);
        Assert.Null(proof); // Signature check failed before simulation
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Third-party verification (node A generates, node B verifies)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThirdPartyVerification_NodeA_GeneratesProof_NodeB_Verifies_WithoutReplay()
    {
        // ── Node A: builds the run, runs the verifier, generates a divergence proof ──
        var (authority, _, ticks, result) = BuildValidRun(seed: 60, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var verifierNodeA = new ReplayVerifier(authority.ToAuthority());
        var innerA = new StateHasherSimulation(ReplayRunner.CreateInitialState(60));
        var simulationA = new DivergingSimulation(innerA, divergeAtOrAfterTick: ticks[1].Tick);

        var valid = verifierNodeA.TryVerifyRun(ticks, result, simulationA, out var divergenceProof);
        Assert.False(valid);
        Assert.NotNull(divergenceProof);

        // ── Node B: verifies the proof using only the Merkle root (no tick chain or replay) ──
        var isProofValid = ReplayVerifier.VerifyDivergenceProof(divergenceProof, merkleRoot);

        Assert.True(isProofValid,
            "Node B must be able to verify the divergence proof using only the Merkle root.");
    }

    [Fact]
    public void ThirdPartyVerification_TamperedProof_NodeB_Rejects()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 61, tickPositions: [0, 256, 512]);
        var merkleRoot = Convert.FromHexString(result.TickMerkleRoot!);

        var verifierNodeA = new ReplayVerifier(authority.ToAuthority());
        var innerA = new StateHasherSimulation(ReplayRunner.CreateInitialState(61));
        var simulationA = new DivergingSimulation(innerA, divergeAtOrAfterTick: ticks[1].Tick);

        verifierNodeA.TryVerifyRun(ticks, result, simulationA, out var divergenceProof);
        Assert.NotNull(divergenceProof);

        // Tamper with the expected tick hash before sending to node B
        var tamperedProof = divergenceProof with
        {
            ExpectedTickHash = TamperHash(divergenceProof.ExpectedTickHash)
        };

        Assert.False(ReplayVerifier.VerifyDivergenceProof(tamperedProof, merkleRoot),
            "Node B must reject a tampered divergence proof.");
    }

    [Fact]
    public void ThirdPartyVerification_WrongRoot_NodeB_Rejects()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 62, tickPositions: [0, 256, 512]);

        var verifierNodeA = new ReplayVerifier(authority.ToAuthority());
        var innerA = new StateHasherSimulation(ReplayRunner.CreateInitialState(62));
        var simulationA = new DivergingSimulation(innerA, divergeAtOrAfterTick: ticks[1].Tick);

        verifierNodeA.TryVerifyRun(ticks, result, simulationA, out var divergenceProof);
        Assert.NotNull(divergenceProof);

        // Node B uses a wrong (e.g., from a different run) Merkle root
        var wrongRoot = CreateHash(55);

        Assert.False(ReplayVerifier.VerifyDivergenceProof(divergenceProof, wrongRoot),
            "Node B must reject a proof verified against the wrong Merkle root.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Proof leaf index is correct
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateDivergenceProof_LeafIndex_MatchesPositionInChain()
    {
        var (_, _, ticks, _) = BuildValidRun(seed: 70, tickPositions: [0, 256, 512, 768]);

        for (var i = 0; i < ticks.Count; i++)
        {
            var t = ticks[i];
            var expectedHash = TickAuthorityService.ComputeTickLeafHash(
                t.Tick, t.PrevStateHash, t.StateHash, t.InputHash);
            var proof = ReplayVerifier.CreateDivergenceProof(ticks, t.Tick, expectedHash, CreateHash(i + 1));

            Assert.Equal(i, proof.LeafIndex);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "divergence-proof-test")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[SHA256.HashSizeInBytes];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        // Byte at index 15 is set to a non-zero marker so that hashes with seed == 0 are
        // still non-trivially different from an all-zero array and are visually distinct
        // from each other when debugging.
        hash[15] = 0xAB;
        return hash;
    }

    /// <summary>Flips the first byte of a hash copy to simulate tampering.</summary>
    private static byte[] TamperHash(byte[] hash)
    {
        var tampered = (byte[])hash.Clone();
        tampered[0] ^= 0xFF;
        return tampered;
    }

    /// <summary>
    /// Builds a valid run using <see cref="TickAuthorityService.FinalizeRun"/> which
    /// adds checkpoints automatically.
    /// </summary>
    private static (SessionAuthority Authority, TickAuthorityService Service, List<SignedTick> Ticks, SignedRunResult Result)
        BuildValidRun(int seed, long[] tickPositions)
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(seed);
        var ticks = BuildChainAtPositions(authority, state, tickPositions);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var replayHash = CreateHash(seed);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);
        return (authority, service, ticks, result);
    }

    /// <summary>
    /// Builds a valid run signed WITHOUT checkpoints (using the basic overload).
    /// Used to test the no-checkpoint fallback path.
    /// </summary>
    private static (SessionAuthority Authority, TickAuthorityService Service, List<SignedTick> Ticks, SignedRunResult Result)
        BuildValidRunNoCheckpoints(int seed, long[] tickPositions)
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(seed);
        var ticks = BuildChainAtPositions(authority, state, tickPositions);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var replayHash = CreateHash(seed);

        // Compute Merkle root manually and sign with it but no checkpoints.
        var frontier = new MerkleFrontier();
        foreach (var t in ticks)
            frontier.AddLeaf(TickAuthorityService.ComputeTickLeafHash(
                t.Tick, t.PrevStateHash, t.StateHash, t.InputHash));
        var merkleRoot = frontier.ComputeRoot();

        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, merkleRoot);
        return (authority, service, ticks, result);
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
    // Test simulation implementations (same patterns as ReplayVerifierTests)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Honest simulation that computes state hashes using <see cref="StateHasher"/>
    /// with a fixed simulation state.
    /// </summary>
    private sealed class StateHasherSimulation : IDeterministicSimulation
    {
        private readonly SimulationState _state;
        private readonly StateHasher _hasher = new();
        private byte[] _currentHash = new byte[32];
        private long _currentTick = -1;

        public StateHasherSimulation(SimulationState state) => _state = state;

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

    /// <summary>
    /// Wraps an inner simulation and returns a corrupted state hash for all ticks
    /// at or after the specified divergence tick.
    /// </summary>
    private sealed class DivergingSimulation : IDeterministicSimulation
    {
        private readonly IDeterministicSimulation _inner;
        private readonly long _divergeAtOrAfterTick;
        private bool _hasDiverged;

        public DivergingSimulation(IDeterministicSimulation inner, long divergeAtOrAfterTick)
        {
            _inner = inner;
            _divergeAtOrAfterTick = divergeAtOrAfterTick;
        }

        public void Reset()
        {
            _inner.Reset();
            _hasDiverged = false;
        }

        public void ApplyTick(SignedTick tick)
        {
            _inner.ApplyTick(tick);
            if (tick.Tick >= _divergeAtOrAfterTick)
                _hasDiverged = true;
        }

        public byte[] GetStateHash()
        {
            var hash = _inner.GetStateHash();
            if (_hasDiverged)
                hash[0] ^= 0xFF;
            return hash;
        }

        public byte[] SerializeState() => _inner.SerializeState();

        public void LoadState(byte[] state)
        {
            _inner.LoadState(state);
            var loadedTick = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(state.AsSpan(0, 8));
            _hasDiverged = loadedTick >= _divergeAtOrAfterTick;
        }
    }
}
