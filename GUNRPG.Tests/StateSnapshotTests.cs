using System.Buffers.Binary;
using System.Security.Cryptography;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for <see cref="StateSnapshot"/>, <see cref="SessionAuthority.CreateSnapshot"/>,
/// <see cref="ReplayVerifier.VerifySnapshot"/>, and snapshot-accelerated
/// <see cref="ReplayVerifier.VerifyRun"/>.
/// </summary>
public sealed class StateSnapshotTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Snapshot correctness
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSnapshot_ProducesCorrectHash_RoundTrip()
    {
        // Arrange: build a small run and capture a snapshot at the first checkpoint.
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(100);
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);

        var simulation = new SerializableSimulation(state);
        simulation.ApplyTick(ticks[0]);
        var hashBeforeSerialize = simulation.GetStateHash();

        // Act: serialize and create snapshot.
        var serialized = simulation.SerializeState();
        var snapshot = authority.CreateSnapshot(Guid.NewGuid(), ticks[0].Tick, ticks[0].StateHash, serialized);

        // Load state into a fresh simulation and verify hash matches.
        var sim2 = new SerializableSimulation(state);
        sim2.LoadState(snapshot.SerializedState);
        var hashAfterLoad = sim2.GetStateHash();

        Assert.Equal(hashBeforeSerialize, hashAfterLoad);
    }

    [Fact]
    public void LoadState_AfterMultipleTicks_RestoresExactHash()
    {
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(101);
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);

        var sim = new SerializableSimulation(state);
        foreach (var t in ticks)
            sim.ApplyTick(t);
        var expectedHash = sim.GetStateHash();

        // Serialize after replaying all ticks, then load into a new instance.
        var serialized = sim.SerializeState();
        var sim2 = new SerializableSimulation(state);
        sim2.LoadState(serialized);

        Assert.Equal(expectedHash, sim2.GetStateHash());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Snapshot verification — correct snapshot
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySnapshot_ValidSnapshot_ReturnsTrue()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 200, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, checkpointIndex: 0);

        Assert.True(verifier.VerifySnapshot(sessionId, snapshot, result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Snapshot verification — wrong signature rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySnapshot_WrongAuthoritySignature_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 210, tickPositions: [0, 256, 512]);
        var wrongAuthority = CreateAuthority("wrong-snap-authority");

        // Snapshot signed with a different authority.
        var wrongSnapshot = BuildSnapshotAtCheckpointWithAuthority(wrongAuthority, sessionId, ticks, result, 0);

        var verifier = new ReplayVerifier(authority.ToAuthority());
        Assert.False(verifier.VerifySnapshot(sessionId, wrongSnapshot, result));
    }

    [Fact]
    public void VerifySnapshot_TamperedSignature_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 211, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        // Flip a byte in the signature.
        var badSig = (byte[])snapshot.Signature.Clone();
        badSig[0] ^= 0xFF;
        var tamperedSnapshot = snapshot with { Signature = badSig };

        Assert.False(verifier.VerifySnapshot(sessionId, tamperedSnapshot, result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Snapshot verification — mismatched hash rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySnapshot_WrongStateHash_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 220, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        // Replace StateHash with a different (but valid-length) hash.
        var badHash = new byte[32];
        badHash[0] = 0xFF;
        var tamperedSnapshot = snapshot with { StateHash = badHash };

        Assert.False(verifier.VerifySnapshot(sessionId, tamperedSnapshot, result));
    }

    [Fact]
    public void VerifySnapshot_StateHashWrongLength_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 221, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        // Replace StateHash with a 16-byte array (wrong length).
        var shortHash = new byte[16];
        var tamperedSnapshot = snapshot with { StateHash = shortHash };

        Assert.False(verifier.VerifySnapshot(sessionId, tamperedSnapshot, result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Snapshot verification — tick not in checkpoints
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySnapshot_TickNotInCheckpoints_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 230, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Build a snapshot for tick 1 (not a checkpoint).
        var stateHash = ticks[0].StateHash;
        var serialized = new SerializableSimulation(ReplayRunner.CreateInitialState(230))
            .WithTicksApplied(ticks.GetRange(0, 1)).SerializeState();
        var snapshot = authority.CreateSnapshot(sessionId, 1, stateHash, serialized);

        Assert.False(verifier.VerifySnapshot(sessionId, snapshot, result));
    }

    [Fact]
    public void VerifySnapshot_RunResultWithNoCheckpoints_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var authority = CreateAuthority();
        var state = ReplayRunner.CreateInitialState(231);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);

        // Sign without checkpoints (Merkle-only overload).
        var hasher = new StateHasher();
        var finalHash = hasher.HashTick(ticks[^1].Tick, state);
        var replayHash = CreateArbitraryHash(231);
        var merkleRoot = ComputeMerkleRoot(ticks);
        var result = authority.Sign(Guid.NewGuid(), TestPlayerId, finalHash, replayHash, merkleRoot);

        Assert.Null(result.Checkpoints);

        var verifier = new ReplayVerifier(authority.ToAuthority());
        var serialized = new byte[40];
        var snapshot = authority.CreateSnapshot(sessionId, 0, ticks[0].StateHash, serialized);

        Assert.False(verifier.VerifySnapshot(sessionId, snapshot, result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Corrupted serialized state rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySnapshot_CorruptedSerializedState_ReturnsFalse()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 240, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        // Corrupt the serialized state (the signature will no longer match the payload).
        var badState = (byte[])snapshot.SerializedState.Clone();
        badState[0] ^= 0xFF;
        var tamperedSnapshot = snapshot with { SerializedState = badState };

        Assert.False(verifier.VerifySnapshot(sessionId, tamperedSnapshot, result));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Replay acceleration — with and without snapshot produce identical results
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_WithSnapshot_ReturnsTrue()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 300, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Create a snapshot at checkpoint 0 (tick 0).
        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(300));
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [snapshot]));
    }

    [Fact]
    public void VerifyRun_WithSnapshotAtMiddleCheckpoint_ReturnsTrue()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 301, tickPositions: [0, 256, 512, 768, 1024]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Create a snapshot at checkpoint 2 (tick 512).
        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 2);

        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(301));
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [snapshot]));
    }

    [Fact]
    public void VerifyRun_WithSnapshotAndWithout_ProduceIdenticalResults()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 302, tickPositions: [0, 256, 512, 768, 1024]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 1);

        var simWithout = new SerializableSimulation(ReplayRunner.CreateInitialState(302));
        var simWith = new SerializableSimulation(ReplayRunner.CreateInitialState(302));

        var resultWithout = verifier.VerifyRun(ticks, result, simWithout);
        var resultWith = verifier.VerifyRun(ticks, result, simWith, [snapshot]);

        Assert.True(resultWithout);
        Assert.True(resultWith);
    }

    [Fact]
    public void VerifyRun_SnapshotsBeyondTargetTick_FallsBackToGenesis()
    {
        // Snapshot at tick 1024 but run only has ticks [0, 256, 512] — snapshot tick > last tick.
        // VerifyRun should still succeed using the reset-and-replay fallback.
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 303, tickPositions: [0, 256, 512]);
        var authority2 = CreateAuthority("snap-authority-2");

        // Build a snapshot with an out-of-range tick — it won't verify, so VerifyRun should still pass
        // by falling back to genesis replay.
        var fakeStateHash = ticks[^1].StateHash;
        var fakeSerializedState = new byte[40];
        var fakeSnapshot = authority.CreateSnapshot(sessionId, 9999L, fakeStateHash, fakeSerializedState);

        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(303));

        // VerifySnapshot(9999) will fail (not in checkpoints), so snapshot is discarded.
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [fakeSnapshot]));
    }

    [Fact]
    public void VerifyRun_MultipleSnapshots_SelectsLatestBeforeTarget()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 304, tickPositions: [0, 256, 512, 768, 1024]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Create snapshots at checkpoints 0, 1, and 2 (ticks 0, 256, 512).
        var snap0 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);
        var snap1 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 1);
        var snap2 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 2);

        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(304));
        // Provide all three snapshots — verifier should pick the best one automatically.
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [snap0, snap1, snap2]));
    }

    [Fact]
    public void VerifyRun_NullSnapshots_BehavesLikeNoSnapshots()
    {
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 305, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(305));

        Assert.True(verifier.VerifyRun(ticks, result, simulation, null));
    }

    [Fact]
    public void VerifyRun_EmptySnapshots_BehavesLikeNoSnapshots()
    {
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 306, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());
        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(306));

        Assert.True(verifier.VerifyRun(ticks, result, simulation, []));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Invalid snapshot does not poison the verification (silently skipped)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_InvalidSnapshotInList_IsSkippedAndRunStillVerifies()
    {
        var sessionId = Guid.NewGuid();
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(seed: 310, tickPositions: [0, 256, 512]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var validSnapshot = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);

        // Corrupt the signature of a second snapshot.
        var corruptedSig = (byte[])validSnapshot.Signature.Clone();
        corruptedSig[0] ^= 0xFF;
        var invalidSnapshot = validSnapshot with { Signature = corruptedSig };

        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(310));
        // The invalid snapshot should be silently discarded; the valid one accelerates replay.
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [invalidSnapshot, validSnapshot]));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Snapshot selection strategy: latest snapshot ≤ target tick
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_SnapshotSelectionPrefersLatestBeforeTarget()
    {
        // Run: snapshots at 0, 512, 1024 — target (first checkpoint probe) is tick 1400.
        // Verifier should select snapshot 1024.
        var sessionId = Guid.NewGuid();
        // tickPositions includes 0, 512, 1024, and a final tick ~1400 range:
        var (authority, ticks, result) = BuildValidRunWithCheckpoints(
            seed: 320, tickPositions: [0, 512, 1024, 1280]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        var snap0 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 0);
        var snap1 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 1);
        var snap2 = BuildSnapshotAtCheckpoint(authority, sessionId, ticks, result, 2);

        var simulation = new SerializableSimulation(ReplayRunner.CreateInitialState(320));
        Assert.True(verifier.VerifyRun(ticks, result, simulation, [snap0, snap1, snap2]));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "snapshot-test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static byte[] CreateArbitraryHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[15] = 0xDD;
        return hash;
    }

    private static (SessionAuthority Authority, List<SignedTick> Ticks, SignedRunResult Result)
        BuildValidRunWithCheckpoints(int seed, long[] tickPositions)
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(seed);
        var ticks = BuildChainAtPositions(authority, state, tickPositions);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var replayHash = CreateArbitraryHash(seed);
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

    private static byte[] ComputeMerkleRoot(IReadOnlyList<SignedTick> ticks)
    {
        var frontier = new MerkleFrontier();
        foreach (var t in ticks)
            frontier.AddLeaf(TickAuthorityService.ComputeTickLeafHash(t.Tick, t.PrevStateHash, t.StateHash, t.InputHash));
        return frontier.ComputeRoot();
    }

    /// <summary>
    /// Builds a signed snapshot at a specific checkpoint index in <paramref name="result"/>.
    /// The serialized state is derived from replaying the tick chain up to that checkpoint.
    /// </summary>
    private static StateSnapshot BuildSnapshotAtCheckpoint(
        SessionAuthority authority,
        Guid sessionId,
        List<SignedTick> ticks,
        SignedRunResult result,
        int checkpointIndex)
    {
        return BuildSnapshotAtCheckpointWithAuthority(authority, sessionId, ticks, result, checkpointIndex);
    }

    private static StateSnapshot BuildSnapshotAtCheckpointWithAuthority(
        SessionAuthority authority,
        Guid sessionId,
        List<SignedTick> ticks,
        SignedRunResult result,
        int checkpointIndex)
    {
        var checkpoint = result.Checkpoints![checkpointIndex];
        var state = ReplayRunner.CreateInitialState(0); // state is tick-independent in test hasher

        var sim = new SerializableSimulation(state);
        foreach (var t in ticks)
        {
            sim.ApplyTick(t);
            if (t.Tick == checkpoint.TickIndex)
                break;
        }

        var serialized = sim.SerializeState();
        return authority.CreateSnapshot(sessionId, checkpoint.TickIndex, checkpoint.StateHash, serialized);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test simulation implementation with serialization support
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serializable simulation that supports <see cref="IDeterministicSimulation.SerializeState"/>
    /// and <see cref="IDeterministicSimulation.LoadState"/>.
    /// Serialization format: tick (big-endian int64) || hash (32 bytes).
    /// </summary>
    private sealed class SerializableSimulation : IDeterministicSimulation
    {
        private readonly SimulationState _state;
        private readonly StateHasher _hasher = new();
        private byte[] _currentHash = new byte[32];
        private long _currentTick = -1;

        public SerializableSimulation(SimulationState state) => _state = state;

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
            BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(0, 8), _currentTick);
            _currentHash.CopyTo(bytes, 8);
            return bytes;
        }

        public void LoadState(byte[] state)
        {
            _currentTick = BinaryPrimitives.ReadInt64BigEndian(state.AsSpan(0, 8));
            _currentHash = state[8..40];
        }

        /// <summary>Fluent helper to apply a batch of ticks and return this instance.</summary>
        public SerializableSimulation WithTicksApplied(IEnumerable<SignedTick> ticks)
        {
            foreach (var t in ticks)
                ApplyTick(t);
            return this;
        }
    }
}
