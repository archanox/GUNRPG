using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the Merkle checkpoint system:
/// - RunCheckpoint record
/// - SignedRunResult.Checkpoints
/// - TickAuthorityService.CheckpointInterval and checkpoint generation in FinalizeRun
/// - TickAuthorityService.VerifyCheckpoints (fast-path verification)
/// - Cryptographic binding of checkpoints in the authority signature
/// - Tampering detection
/// - Interval edge cases
/// </summary>
public sealed class MerkleCheckpointTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // 1. Deterministic Checkpoints
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_ProducesIdenticalCheckpoints_WhenRunTwice()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        // Build a chain spanning more than one CheckpointInterval (ticks 0, 256, 512).
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(99);

        var result1 = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);
        var result2 = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        // Both runs must produce the same number of checkpoints.
        Assert.NotNull(result1.Checkpoints);
        Assert.NotNull(result2.Checkpoints);
        Assert.Equal(result1.Checkpoints.Count, result2.Checkpoints.Count);

        // Each checkpoint must have the same tick index and state hash.
        for (var i = 0; i < result1.Checkpoints.Count; i++)
        {
            Assert.Equal(result1.Checkpoints[i].TickIndex, result2.Checkpoints[i].TickIndex);
            Assert.True(
                result1.Checkpoints[i].StateHash.AsSpan().SequenceEqual(result2.Checkpoints[i].StateHash),
                $"Checkpoint[{i}] StateHash mismatch between two identical runs.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Checkpoint positions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_CheckpointsAtIntervalBoundaries()
    {
        // Ticks at 0, 256, 512 are all multiples of CheckpointInterval.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(1);

        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(1);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.Checkpoints);
        // All three ticks are at interval boundaries, so all three become checkpoints.
        Assert.Equal(3, result.Checkpoints.Count);
        Assert.Equal(0, result.Checkpoints[0].TickIndex);
        Assert.Equal(256, result.Checkpoints[1].TickIndex);
        Assert.Equal(512, result.Checkpoints[2].TickIndex);
    }

    [Fact]
    public void FinalizeRun_FinalTickAlwaysCheckpointed_EvenIfNotAtBoundary()
    {
        // Ticks: only tick 0 is at an interval boundary; tick 7 is the final tick.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(2);

        var ticks = BuildChainAtPositions(authority, state, [0, 7]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(2);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.Checkpoints);
        // Tick 0 → interval boundary; tick 7 → final tick (not a boundary but always added).
        Assert.Equal(2, result.Checkpoints.Count);
        Assert.Equal(0, result.Checkpoints[0].TickIndex);
        Assert.Equal(7, result.Checkpoints[1].TickIndex);
    }

    [Fact]
    public void FinalizeRun_SingleTickChain_OneCheckpoint()
    {
        // A chain with only tick 0: it is both the first and the last tick.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(3);

        var ticks = BuildChainAtPositions(authority, state, [0]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(3);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.Checkpoints);
        // Tick 0 satisfies BOTH conditions (0 % 256 == 0 AND it is the last tick) → one entry.
        Assert.Single(result.Checkpoints);
        Assert.Equal(0, result.Checkpoints[0].TickIndex);
    }

    [Fact]
    public void FinalizeRun_RunLengthLessThanInterval_CheckpointsAtStartAndEnd()
    {
        // Ticks at 0 and 100 (both less than CheckpointInterval=256).
        // Tick 0: interval boundary; tick 100: final tick only.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(4);

        var ticks = BuildChainAtPositions(authority, state, [0, 100]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(4);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.Checkpoints);
        Assert.Equal(2, result.Checkpoints.Count);
        Assert.Equal(0, result.Checkpoints[0].TickIndex);
        Assert.Equal(100, result.Checkpoints[1].TickIndex);
    }

    [Fact]
    public void FinalizeRun_FinalTickAtExactBoundary_NoCheckpointDuplicated()
    {
        // Tick 256 is both an interval boundary and the final tick.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(5);

        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(5);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.Checkpoints);
        // Tick 0 → boundary; tick 256 → boundary AND final, but added only once.
        Assert.Equal(2, result.Checkpoints.Count);
        Assert.Equal(0, result.Checkpoints[0].TickIndex);
        Assert.Equal(256, result.Checkpoints[1].TickIndex);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Cryptographic binding – signature covers checkpoints
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySignedRun_WithCheckpoints_Passes()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(10);

        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(10), ticks);

        Assert.NotNull(result.Checkpoints);
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void VerifySignedRun_TamperedCheckpointHash_Fails()
    {
        // Build a valid result, then re-create SignedRunResult with a tampered checkpoint.
        // The original signature does not cover the tampered payload → verification must fail.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(11);

        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var validResult = service.FinalizeRun(
            Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(11), ticks);

        Assert.NotNull(validResult.Checkpoints);
        Assert.True(SessionAuthority.VerifySignedRun(validResult, authority.ToAuthority()),
            "The unmodified result should verify correctly before tampering.");

        // Build tampered checkpoints: same tick indices but wrong state hashes.
        var tampered = validResult.Checkpoints
            .Select(cp =>
            {
                var bad = (byte[])cp.StateHash.Clone();
                bad[0] ^= 0xFF; // flip first byte
                return new RunCheckpoint(cp.TickIndex, bad);
            })
            .ToList();

        // Re-create SignedRunResult with the original signature but tampered checkpoints.
        var tamperedResult = new SignedRunResult(
            validResult.SessionId,
            validResult.PlayerId,
            validResult.FinalHash,
            validResult.AuthorityId,
            validResult.Signature,
            validResult.ReplayHash,
            validResult.TickMerkleRoot,
            tampered);

        // The payload hash for the tampered result differs → signature verification fails.
        Assert.False(SessionAuthority.VerifySignedRun(tamperedResult, authority.ToAuthority()),
            "Verification must fail when a checkpoint state hash is tampered.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. VerifyCheckpoints – fast-path replay verification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyCheckpoints_AllMatch_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(20);

        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(20), ticks);

        // All checkpoint state hashes were taken from the same tick chain, so they must match.
        Assert.True(TickAuthorityService.VerifyCheckpoints(result, ticks));
    }

    [Fact]
    public void VerifyCheckpoints_StateHashMismatch_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(21);

        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(21), ticks);

        // Tamper one tick's state hash in the chain.
        var tamperedTick = ticks[0];
        var badStateHash = (byte[])tamperedTick.StateHash.Clone();
        badStateHash[0] ^= 0xFF;
        var tamperedChain = new List<SignedTick>(ticks)
        {
            [0] = new SignedTick(
                tamperedTick.Tick,
                tamperedTick.PrevStateHash,
                badStateHash,
                tamperedTick.InputHash,
                tamperedTick.Signature)
        };

        Assert.False(TickAuthorityService.VerifyCheckpoints(result, tamperedChain),
            "VerifyCheckpoints must return false when a tick's state hash does not match the checkpoint.");
    }

    [Fact]
    public void VerifyCheckpoints_MissingTickInChain_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(22);

        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(22), ticks);

        // Remove tick 0 from the chain so the checkpoint at tick 0 cannot be verified.
        var truncatedChain = ticks.Skip(1).ToList();

        Assert.False(TickAuthorityService.VerifyCheckpoints(result, truncatedChain),
            "VerifyCheckpoints must return false when a checkpoint tick is absent from the chain.");
    }

    [Fact]
    public void VerifyCheckpoints_NoCheckpoints_ReturnsTrue()
    {
        // A result signed without checkpoints (legacy Merkle-only overload) should pass.
        var authority = CreateAuthority();
        var finalHash = CreateHash(30);
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(31), CreateHash(32));

        Assert.Null(result.Checkpoints);
        Assert.True(TickAuthorityService.VerifyCheckpoints(result, new List<SignedTick>()));
    }

    [Fact]
    public void VerifyCheckpoints_EmptyCheckpointList_ReturnsTrue()
    {
        // Explicitly construct a SignedRunResult with an empty checkpoints list.
        var authority = CreateAuthority();
        var finalHash = CreateHash(40);
        var merkleRoot = CreateHash(41);
        var replayHash = CreateHash(42);

        var result = authority.Sign(
            Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash, merkleRoot,
            new List<RunCheckpoint>());

        Assert.NotNull(result.Checkpoints);
        Assert.Empty(result.Checkpoints);
        Assert.True(TickAuthorityService.VerifyCheckpoints(result, new List<SignedTick>()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. SignedRunResult validation – checkpoint constructor guards
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignedRunResult_Checkpoints_WithoutTickMerkleRoot_Throws()
    {
        var authority = CreateAuthority();
        var signature = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(50)).Signature;

        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            new string('A', 64), "auth", signature,
            replayHash: null,
            tickMerkleRoot: null,
            checkpoints: [new RunCheckpoint(0, CreateHash(51))]));
    }

    [Fact]
    public void SignedRunResult_NullCheckpointInList_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(60);
        var signature = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(61), CreateHash(62)).Signature;
        var merkleRootHex = Convert.ToHexString(CreateHash(62));

        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToHexString(finalHash), "auth", signature,
            Convert.ToHexString(CreateHash(61)),
            merkleRootHex,
            checkpoints: [null!]));
    }

    [Fact]
    public void SignedRunResult_CheckpointWithNegativeTickIndex_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(70);
        var sig = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(71), CreateHash(72)).Signature;

        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToHexString(finalHash), "auth", sig,
            Convert.ToHexString(CreateHash(71)),
            Convert.ToHexString(CreateHash(72)),
            checkpoints: [new RunCheckpoint(-1L, CreateHash(73))]));
    }

    [Fact]
    public void SignedRunResult_CheckpointWithInvalidStateHash_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(80);
        var sig = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(81), CreateHash(82)).Signature;

        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToHexString(finalHash), "auth", sig,
            Convert.ToHexString(CreateHash(81)),
            Convert.ToHexString(CreateHash(82)),
            checkpoints: [new RunCheckpoint(0, new byte[16])]));   // wrong size
    }

    [Fact]
    public void SignedRunResult_OutOfOrderCheckpoints_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(83);
        var sig = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(84), CreateHash(85)).Signature;

        // Checkpoint tick indices are out of order: 256 comes before 0.
        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToHexString(finalHash), "auth", sig,
            Convert.ToHexString(CreateHash(84)),
            Convert.ToHexString(CreateHash(85)),
            checkpoints:
            [
                new RunCheckpoint(256, CreateHash(86)),
                new RunCheckpoint(0, CreateHash(87))   // out of order
            ]));
    }

    [Fact]
    public void SignedRunResult_DuplicateCheckpointTickIndex_Throws()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(88);
        var sig = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), finalHash, CreateHash(89), CreateHash(90)).Signature;

        // Two checkpoints share the same tick index.
        Assert.Throws<ArgumentException>(() => new SignedRunResult(
            Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToHexString(finalHash), "auth", sig,
            Convert.ToHexString(CreateHash(89)),
            Convert.ToHexString(CreateHash(90)),
            checkpoints:
            [
                new RunCheckpoint(256, CreateHash(91)),
                new RunCheckpoint(256, CreateHash(92))  // duplicate
            ]));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Checkpoint state hashes match the tick chain
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_CheckpointStateHashes_MatchSignedTickStateHashes()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(90);

        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), ticks[^1].StateHash, CreateHash(90), ticks);

        Assert.NotNull(result.Checkpoints);

        // Build a lookup of tick → signed tick from the chain.
        var tickMap = ticks.ToDictionary(t => t.Tick);

        foreach (var checkpoint in result.Checkpoints)
        {
            Assert.True(tickMap.TryGetValue(checkpoint.TickIndex, out var signedTick),
                $"Checkpoint tick {checkpoint.TickIndex} not found in the tick chain.");
            Assert.True(
                checkpoint.StateHash.AsSpan().SequenceEqual(signedTick!.StateHash),
                $"Checkpoint[{checkpoint.TickIndex}].StateHash does not match the SignedTick.StateHash.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. CheckpointInterval constant
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckpointInterval_Is256()
    {
        Assert.Equal(256, TickAuthorityService.CheckpointInterval);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static readonly Guid TestPlayerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xCC;
        return hash;
    }

    private static SessionAuthority CreateAuthority(string id = "checkpoint-test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    /// <summary>
    /// Builds a signed tick chain at the exact tick positions specified.
    /// The tick chain is hash-linked: each tick's PrevStateHash equals the
    /// previous tick's StateHash.
    /// </summary>
    private static List<SignedTick> BuildChainAtPositions(
        SessionAuthority authority,
        SimulationState state,
        long[] tickPositions)
    {
        var hasher = new StateHasher();
        var ticks = new List<SignedTick>(tickPositions.Length);
        var prev = TickAuthorityService.GenesisStateHash;
        var playerId = TestPlayerId;

        foreach (var tickPos in tickPositions)
        {
            var stateHash = hasher.HashTick(tickPos, state);
            var inputHash = new TickInputs(tickPos, [new PlayerInput(playerId, new ExfilAction())]).ComputeHash();
            var sig = authority.SignTick(tickPos, prev, stateHash, inputHash);
            ticks.Add(new SignedTick(tickPos, prev, stateHash, inputHash, sig));
            prev = stateHash;
        }

        return ticks;
    }
}
