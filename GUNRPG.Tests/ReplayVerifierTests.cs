using System.Security.Cryptography;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for <see cref="ReplayVerifier"/>:
/// - Valid run: binary search verifies all checkpoints successfully.
/// - Early divergence: simulation diverges at the first checkpoint; detected immediately.
/// - Late divergence: simulation diverges only at the final checkpoint; binary search isolates the last segment.
/// - Random divergence (middle): divergence in the middle checkpoint; binary search detects it.
/// - Invalid signature: verification returns false before any simulation.
/// - Invalid Merkle root: verification returns false after signature check.
/// - No checkpoints: falls back to direct full-replay verification.
/// </summary>
public sealed class ReplayVerifierTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Valid Run
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_ValidRun_ReturnsTrue()
    {
        var (authority, service, ticks, result) = BuildValidRun(seed: 1, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(1));

        Assert.True(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_ValidRun_SingleCheckpoint_ReturnsTrue()
    {
        // A chain with only one tick (= only one checkpoint = first & final)
        var (authority, service, ticks, result) = BuildValidRun(seed: 2, tickPositions: [0]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(2));

        Assert.True(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_ValidRun_ManyCheckpoints_ReturnsTrue()
    {
        // Longer run: 0, 256, 512, 768, 1024
        var (authority, service, ticks, result) = BuildValidRun(seed: 3, tickPositions: [0, 256, 512, 768, 1024]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(3));

        Assert.True(verifier.VerifyRun(ticks, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Early Divergence
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_EarlyDivergence_AtFirstCheckpoint_ReturnsFalse()
    {
        // The simulation diverges from the very first tick (tick 0).
        // VerifyRun should fail immediately when verifying the first checkpoint.
        var (authority, service, ticks, result) = BuildValidRun(seed: 10, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(10));
        // Diverge at tick 0 (the first tick in the chain)
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[0].Tick);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_EarlyDivergence_BeforeSecondCheckpoint_ReturnsFalse()
    {
        // Diverge at tick 1 (after first checkpoint tick 0 but before second checkpoint tick 256).
        // The binary search should detect it in the [C0, C1] window.
        var (authority, service, ticks, result) = BuildValidRun(
            seed: 11,
            tickPositions: [0, 1, 256, 512]); // tick 1 is an intermediate tick between checkpoints

        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(11));
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: 1);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Late Divergence
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_LateDivergence_AtFinalCheckpoint_ReturnsFalse()
    {
        // Diverge at the very last tick. The binary search should narrow to the last window.
        var tickPositions = new long[] { 0, 256, 512, 768 };
        var (authority, service, ticks, result) = BuildValidRun(seed: 20, tickPositions: tickPositions);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(20));
        // Diverge at the last tick index (768)
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: ticks[^1].Tick);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_LateDivergence_PenultimateWindow_ReturnsFalse()
    {
        var tickPositions = new long[] { 0, 256, 512, 768, 1024 };
        var (authority, service, ticks, result) = BuildValidRun(seed: 21, tickPositions: tickPositions);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(21));
        // Diverge in the second-to-last interval (at tick 768)
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: 768);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Random (Middle) Divergence
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_MiddleDivergence_ReturnsFalse()
    {
        // Diverge at the middle checkpoint.
        var tickPositions = new long[] { 0, 256, 512, 768, 1024 };
        var (authority, service, ticks, result) = BuildValidRun(seed: 30, tickPositions: tickPositions);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(30));
        // Diverge at tick 512 (middle of five checkpoints)
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: 512);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_RandomDivergence_DetectedRegardlessOfPosition()
    {
        // Build a run with 7 checkpoints and test divergence at each non-first position.
        var tickPositions = new long[] { 0, 256, 512, 768, 1024, 1280, 1536 };
        var (authority, service, ticks, result) = BuildValidRun(seed: 31, tickPositions: tickPositions);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        for (var divergeIndex = 1; divergeIndex < tickPositions.Length; divergeIndex++)
        {
            var inner = new StateHasherSimulation(ReplayRunner.CreateInitialState(31));
            var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: tickPositions[divergeIndex]);

            Assert.False(
                verifier.VerifyRun(ticks, result, simulation),
                $"VerifyRun should return false when simulation diverges at tick {tickPositions[divergeIndex]}.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Invalid Signature
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_WrongAuthority_ReturnsFalse()
    {
        var (authority, service, ticks, result) = BuildValidRun(seed: 40, tickPositions: [0, 256]);
        // Use a different (unrelated) authority for verification
        var wrongPrivateKey = SessionAuthority.GeneratePrivateKey();
        var wrongAuthority = new SessionAuthority(wrongPrivateKey, "wrong-authority").ToAuthority();

        var verifier = new ReplayVerifier(wrongAuthority);
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(40));

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Invalid Merkle Root
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_TamperedTickChain_MerkleRootMismatch_ReturnsFalse()
    {
        var (authority, service, ticks, result) = BuildValidRun(seed: 50, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(50));

        // Replace the first tick with a modified version (flip a bit in StateHash).
        // This breaks the Merkle root but leaves everything else intact.
        var originalTick = ticks[0];
        var badStateHash = (byte[])originalTick.StateHash.Clone();
        badStateHash[0] ^= 0xFF;
        var tamperedTick = new SignedTick(
            originalTick.Tick,
            originalTick.PrevStateHash,
            badStateHash,
            originalTick.InputHash,
            originalTick.Signature);

        var tamperedChain = new List<SignedTick>(ticks) { [0] = tamperedTick };

        Assert.False(verifier.VerifyRun(tamperedChain, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. No Checkpoints (fallback to direct replay)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_NoCheckpoints_ValidSimulation_ReturnsTrue()
    {
        // Build a SignedRunResult using the Merkle overload (no checkpoints).
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(60);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);

        var hasher = new StateHasher();
        var finalStateHash = hasher.HashTick(ticks[^1].Tick, state);
        var replayHash = CreateArbitraryHash(60);
        var merkleRoot = ComputeMerkleRoot(ticks);

        // Sign with the Merkle overload (no checkpoints parameter)
        var result = authority.Sign(
            Guid.NewGuid(), Guid.NewGuid(),
            finalStateHash, replayHash, merkleRoot);

        Assert.Null(result.Checkpoints);

        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(state);

        Assert.True(verifier.VerifyRun(ticks, result, simulation));
    }

    [Fact]
    public void VerifyRun_NoCheckpoints_DivergingSimulation_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(61);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);

        var hasher = new StateHasher();
        var finalStateHash = hasher.HashTick(ticks[^1].Tick, state);
        var replayHash = CreateArbitraryHash(61);
        var merkleRoot = ComputeMerkleRoot(ticks);

        var result = authority.Sign(
            Guid.NewGuid(), Guid.NewGuid(),
            finalStateHash, replayHash, merkleRoot);

        var verifier = new ReplayVerifier(authority.ToAuthority());
        var inner = new StateHasherSimulation(state);
        // Always diverge (from tick 0)
        var simulation = new DivergingSimulation(inner, divergeAtOrAfterTick: 0);

        Assert.False(verifier.VerifyRun(ticks, result, simulation));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Tick chain ordering validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_OutOfOrderTickChain_ReturnsFalse()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 70, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(70));

        // Swap the second and third ticks to break ordering.
        var reordered = new List<SignedTick>(ticks) { [1] = ticks[2], [2] = ticks[1] };

        Assert.False(verifier.VerifyRun(reordered, result, simulation),
            "VerifyRun must return false when tick chain is not strictly ordered.");
    }

    [Fact]
    public void VerifyRun_DuplicateTickInChain_ReturnsFalse()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 71, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(71));

        // Insert a duplicate of tick[1] at position 2 (same Tick value as index 1).
        var withDuplicate = new List<SignedTick> { ticks[0], ticks[1], ticks[1], ticks[2] };

        Assert.False(verifier.VerifyRun(withDuplicate, result, simulation),
            "VerifyRun must return false when the tick chain contains duplicate tick indices.");
    }

    [Fact]
    public void VerifyRun_NullElementInTickChain_ReturnsFalse()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 72, tickPositions: [0, 256]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new StateHasherSimulation(ReplayRunner.CreateInitialState(72));

        // Insert a null element into the tick chain.
        var withNull = new List<SignedTick> { ticks[0], null!, ticks[1] };

        Assert.False(verifier.VerifyRun(withNull, result, simulation),
            "VerifyRun must return false when the tick chain contains a null entry.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. State hash length validation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_SimulationReturnsWrongLengthHash_ReturnsFalse()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 80, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Simulation that always returns a 16-byte hash instead of 32 bytes.
        var simulation = new WrongLengthHashSimulation(hashLength: 16);

        Assert.False(verifier.VerifyRun(ticks, result, simulation),
            "VerifyRun must return false when GetStateHash returns a hash that is not 32 bytes.");
    }

    [Fact]
    public void VerifyRun_SimulationReturnsEmptyHash_ReturnsFalse()
    {
        var (authority, _, ticks, result) = BuildValidRun(seed: 81, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var simulation = new WrongLengthHashSimulation(hashLength: 0);

        Assert.False(verifier.VerifyRun(ticks, result, simulation),
            "VerifyRun must return false when GetStateHash returns an empty byte array.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "replay-verifier-test")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static byte[] CreateArbitraryHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xDD;
        return hash;
    }

    /// <summary>
    /// Builds a chain of signed ticks at the specified tick positions,
    /// signs a <see cref="SignedRunResult"/> with checkpoints, and returns all components.
    /// </summary>
    private static (SessionAuthority Authority, TickAuthorityService Service, List<SignedTick> Ticks, SignedRunResult Result)
        BuildValidRun(int seed, long[] tickPositions)
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(seed);
        var ticks = BuildChainAtPositions(authority, state, tickPositions);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var replayHash = CreateArbitraryHash(seed);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);
        return (authority, service, ticks, result);
    }

    /// <summary>
    /// Builds a hash-linked signed tick chain at exactly the specified tick positions.
    /// </summary>
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

    private static byte[] ComputeMerkleRoot(IReadOnlyList<SignedTick> ticks)
    {
        var frontier = new MerkleFrontier();
        foreach (var t in ticks)
            frontier.AddLeaf(TickAuthorityService.ComputeTickLeafHash(t.Tick, t.PrevStateHash, t.StateHash, t.InputHash));
        return frontier.ComputeRoot();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test simulation implementations
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Honest simulation that computes state hashes using <see cref="StateHasher"/>
    /// with a fixed simulation state. Produces the same hashes as
    /// <see cref="BuildChainAtPositions"/> when using the same state and seed.
    /// </summary>
    private sealed class StateHasherSimulation : IDeterministicSimulation
    {
        private readonly SimulationState _state;
        private readonly StateHasher _hasher = new();
        private byte[] _currentHash = new byte[32];

        public StateHasherSimulation(SimulationState state) => _state = state;

        public void Reset() => _currentHash = new byte[32];

        public void ApplyTick(SignedTick tick) =>
            _currentHash = _hasher.HashTick(tick.Tick, _state);

        public byte[] GetStateHash() => (byte[])_currentHash.Clone();
    }

    /// <summary>
    /// Wraps an inner simulation and returns a corrupted state hash for all ticks
    /// at or after <see cref="_divergeAtOrAfterTick"/>.
    /// Used to simulate a node that produces wrong results after a specific tick.
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
                hash[0] ^= 0xFF; // flip a bit to simulate divergence
            return hash;
        }
    }

    /// <summary>
    /// Simulation that always returns a hash of a fixed (wrong) length,
    /// used to test that <see cref="ReplayVerifier"/> rejects invalid hash sizes.
    /// </summary>
    private sealed class WrongLengthHashSimulation : IDeterministicSimulation
    {
        private readonly int _hashLength;

        public WrongLengthHashSimulation(int hashLength) => _hashLength = hashLength;

        public void Reset() { }
        public void ApplyTick(SignedTick tick) { }
        public byte[] GetStateHash() => new byte[_hashLength];
    }
}
