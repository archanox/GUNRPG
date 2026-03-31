using System.Text.Json;
using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for authority-signed <see cref="MerkleCheckpoint"/>:
/// <list type="bullet">
///   <item>Checkpoint signing: authority node produces valid checkpoint.</item>
///   <item>Checkpoint verification: valid authority → accepted; unknown authority → rejected; tampered → rejected.</item>
///   <item>Replay resume: verify from tick 0 and from a checkpoint; results identical.</item>
///   <item>Checkpoint ordering: strict increasing, reject duplicates / out-of-order.</item>
///   <item>Checkpoint storage: round-trip JSON serialisation and <see cref="MerkleCheckpointStore"/>.</item>
/// </list>
/// </summary>
public sealed class AuthoritySignedCheckpointTests
{
    private static readonly Guid TestPlayerId = Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Checkpoint signing
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateMerkleCheckpoint_AuthorityNode_ProducesValidCheckpoint()
    {
        var authority = CreateAuthority();
        var merkleRoot = CreateHash(1);

        var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, merkleRoot);

        Assert.Equal(1024UL, checkpoint.Tick);
        Assert.Equal(32, checkpoint.MerkleRoot.Length);
        Assert.Equal(32, checkpoint.AuthorityPublicKey.Length);
        Assert.Equal(64, checkpoint.Signature.Length);
        Assert.True(checkpoint.HasValidStructure);

        // The public key in the checkpoint must match the authority's public key.
        Assert.True(checkpoint.AuthorityPublicKey.AsSpan().SequenceEqual(authority.PublicKey));
    }

    [Fact]
    public void CreateMerkleCheckpoint_MerkleRootIsCloned()
    {
        var authority = CreateAuthority();
        var merkleRoot = CreateHash(2);
        var checkpoint = authority.CreateMerkleCheckpoint(tick: 0, merkleRoot);

        // Mutating the original array must not affect the stored MerkleRoot.
        merkleRoot[0] ^= 0xFF;
        Assert.False(checkpoint.MerkleRoot.AsSpan().SequenceEqual(merkleRoot));
    }

    [Fact]
    public void CreateMerkleCheckpoint_NullMerkleRoot_Throws()
    {
        var authority = CreateAuthority();
        Assert.Throws<ArgumentNullException>(() => authority.CreateMerkleCheckpoint(tick: 0, null!));
    }

    [Fact]
    public void CreateMerkleCheckpoint_WrongLengthMerkleRoot_Throws()
    {
        var authority = CreateAuthority();
        Assert.Throws<ArgumentException>(() =>
            authority.CreateMerkleCheckpoint(tick: 0, new byte[16]));
    }

    [Fact]
    public void AuthorityCheckpointInterval_Is1024()
    {
        Assert.Equal(1024, TickAuthorityService.AuthorityCheckpointInterval);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Checkpoint verification – valid authority signature → accepted
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyMerkleCheckpoint_ValidSignature_TrustedAuthority_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var merkleRoot = CreateHash(10);

        var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, merkleRoot);

        Assert.True(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_ValidSignature_MultipleAuthorities_ReturnsTrue()
    {
        var authority1 = CreateAuthority("auth-1");
        var authority2 = CreateAuthority("auth-2");
        // Registry trusts both authorities.
        var registry = new AuthorityRegistry([authority1.PublicKey, authority2.PublicKey]);
        var merkleRoot = CreateHash(11);

        var checkpoint = authority2.CreateMerkleCheckpoint(tick: 2048, merkleRoot);

        Assert.True(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. Checkpoint verification – unknown authority → rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyMerkleCheckpoint_UnknownAuthority_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var unknownAuthority = CreateAuthority("unknown");
        // Registry only trusts the first authority.
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var merkleRoot = CreateHash(20);

        // Checkpoint signed by an authority NOT in the registry.
        var checkpoint = unknownAuthority.CreateMerkleCheckpoint(tick: 1024, merkleRoot);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_EmptyRegistry_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = AuthorityRegistry.Empty;
        var checkpoint = authority.CreateMerkleCheckpoint(tick: 0, CreateHash(21));

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(checkpoint, registry));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. Checkpoint verification – tampered signature / payload → rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyMerkleCheckpoint_TamperedSignature_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(30));

        // Flip the first byte of the signature.
        var tamperedSig = (byte[])checkpoint.Signature.Clone();
        tamperedSig[0] ^= 0xFF;
        var tampered = new MerkleCheckpoint(checkpoint.Tick, checkpoint.MerkleRoot, checkpoint.AuthorityPublicKey, tamperedSig);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(tampered, registry));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_TamperedMerkleRoot_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(31));

        // Flip a byte in MerkleRoot – the signature no longer covers this payload.
        var tamperedRoot = (byte[])checkpoint.MerkleRoot.Clone();
        tamperedRoot[0] ^= 0xFF;
        var tampered = new MerkleCheckpoint(checkpoint.Tick, tamperedRoot, checkpoint.AuthorityPublicKey, checkpoint.Signature);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(tampered, registry));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_TamperedTick_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(32));

        // Change the tick without re-signing.
        var tampered = new MerkleCheckpoint(9999UL, checkpoint.MerkleRoot, checkpoint.AuthorityPublicKey, checkpoint.Signature);

        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(tampered, registry));
    }

    [Fact]
    public void VerifyMerkleCheckpoint_InvalidStructure_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var registry = new AuthorityRegistry([authority.PublicKey]);

        // Checkpoint with a wrong-length MerkleRoot (16 bytes instead of 32).
        var badRoot = new MerkleCheckpoint(1024UL, new byte[16], authority.PublicKey, new byte[64]);
        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(badRoot, registry));

        // Checkpoint with a wrong-length Signature.
        var badSig = new MerkleCheckpoint(1024UL, CreateHash(33), authority.PublicKey, new byte[32]);
        Assert.False(SessionAuthority.VerifyMerkleCheckpoint(badSig, registry));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. Replay resume – verify from tick 0 vs from checkpoint tick → identical results
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_FromCheckpointTick_ReturnsTrueForValidRun()
    {
        // Build a valid run with checkpoints at 0, 256, 512.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(40);
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(40), ticks);

        // Use the checkpoint at tick 256 (the second RunCheckpoint, index 1).
        Assert.NotNull(result.Checkpoints);
        var cpTick = (ulong)result.Checkpoints![1].TickIndex;
        Assert.Equal(256UL, cpTick); // sanity-check the test assumption
        var cpRoot = result.Checkpoints[1].StateHash;
        var merkleCheckpoint = authority.CreateMerkleCheckpoint(cpTick, cpRoot);

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Verify from tick 0 (baseline).
        var simulationFull = new StateHasherSimulation(state);
        var resultFull = verifier.VerifyRun(ticks, result, simulationFull);

        // Verify from checkpoint tick (should be identical).
        var simulationFromCp = new StateHasherSimulation(state);
        var resultFromCp = verifier.VerifyRun(ticks, result, simulationFromCp, merkleCheckpoint, registry);

        Assert.True(resultFull, "Full replay verification must pass.");
        Assert.True(resultFromCp, "Checkpoint-resumed verification must also pass.");
    }

    [Fact]
    public void VerifyRun_FromCheckpointTick_EquivalentToFullReplay()
    {
        // Full replay and checkpoint-resume must both return the same result for valid runs.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(41);
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512, 768]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(41), ticks);

        Assert.NotNull(result.Checkpoints);

        // Use the checkpoint at tick 512 (index 2).
        var cpTick = (ulong)result.Checkpoints![2].TickIndex;
        Assert.Equal(512UL, cpTick); // sanity-check the test assumption
        var cpRoot = result.Checkpoints[2].StateHash;
        var merkleCheckpoint = authority.CreateMerkleCheckpoint(cpTick, cpRoot);

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        Assert.True(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state)),
            "Full replay must pass.");
        Assert.True(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), merkleCheckpoint, registry),
            "Checkpoint-resumed verification must also pass.");
    }

    [Fact]
    public void VerifyRun_FromCheckpointTick_NullCheckpoint_BehavesLikeFullReplay()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(42), ticks);

        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Passing null checkpoint should behave identically to the 3-parameter overload.
        var r1 = verifier.VerifyRun(ticks, result, new StateHasherSimulation(state));
        var r2 = verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), null);

        Assert.Equal(r1, r2);
    }

    [Fact]
    public void VerifyRun_DivergingAfterCheckpoint_ReturnsFalseForBothReplayModes()
    {
        // Divergence after the checkpoint tick must be detected regardless of whether
        // we start from tick 0 or from the checkpoint.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(43);
        var ticks = BuildChainAtPositions(authority, state, [0, 256, 512]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(43), ticks);

        // Use the checkpoint at tick 256 (the second RunCheckpoint, index 1).
        Assert.NotNull(result.Checkpoints);
        var cpTick = (ulong)result.Checkpoints![1].TickIndex;
        Assert.Equal(256UL, cpTick); // sanity-check the test assumption
        var cpRoot = result.Checkpoints[1].StateHash;
        var merkleCheckpoint = authority.CreateMerkleCheckpoint(cpTick, cpRoot);

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        // Diverge after tick 256 (the checkpoint).
        var divSim1 = new DivergingSimulation(new StateHasherSimulation(state), divergeAtOrAfterTick: 512);
        var divSim2 = new DivergingSimulation(new StateHasherSimulation(state), divergeAtOrAfterTick: 512);

        Assert.False(verifier.VerifyRun(ticks, result, divSim1),
            "Full replay must detect divergence after checkpoint tick.");
        Assert.False(verifier.VerifyRun(ticks, result, divSim2, merkleCheckpoint, registry),
            "Checkpoint-resumed verification must also detect divergence after checkpoint tick.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. Checkpoint validation in VerifyRun – rejections
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyRun_TamperedCheckpointSignature_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(50);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(50), ticks);

        Assert.NotNull(result.Checkpoints);
        var cpTick = (ulong)result.Checkpoints![0].TickIndex;
        var cpRoot = result.Checkpoints[0].StateHash;
        var checkpoint = authority.CreateMerkleCheckpoint(cpTick, cpRoot);

        // Tamper with the signature.
        var badSig = (byte[])checkpoint.Signature.Clone();
        badSig[0] ^= 0xFF;
        var tampered = new MerkleCheckpoint(checkpoint.Tick, checkpoint.MerkleRoot, checkpoint.AuthorityPublicKey, badSig);

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        Assert.False(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), tampered, registry));
    }

    [Fact]
    public void VerifyRun_CheckpointFromUnknownAuthority_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var unknownAuthority = CreateAuthority("unknown");
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(51);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(51), ticks);

        Assert.NotNull(result.Checkpoints);
        var cpTick = (ulong)result.Checkpoints![0].TickIndex;
        var cpRoot = result.Checkpoints[0].StateHash;
        // Checkpoint signed by a different, unknown authority.
        var checkpoint = unknownAuthority.CreateMerkleCheckpoint(cpTick, cpRoot);

        // Registry only trusts the run's authority.
        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        Assert.False(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), checkpoint, registry));
    }

    [Fact]
    public void VerifyRun_CheckpointTickNotInRunResult_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(52);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(52), ticks);

        // Create a checkpoint at tick 128 – NOT present in result.Checkpoints (which has 0 and 256).
        var merkleCheckpoint = authority.CreateMerkleCheckpoint(128, CreateHash(53));

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        Assert.False(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), merkleCheckpoint, registry));
    }

    [Fact]
    public void VerifyRun_CheckpointMerkleRootMismatch_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(53);
        var ticks = BuildChainAtPositions(authority, state, [0, 256]);
        var finalStateHash = new StateHasher().HashTick(ticks[^1].Tick, state);
        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, CreateHash(53), ticks);

        Assert.NotNull(result.Checkpoints);
        var cpTick = (ulong)result.Checkpoints![0].TickIndex;

        // Sign a checkpoint with a WRONG MerkleRoot (not matching the RunCheckpoint.StateHash).
        var wrongRoot = CreateHash(99);
        var checkpoint = authority.CreateMerkleCheckpoint(cpTick, wrongRoot);

        var registry = new AuthorityRegistry([authority.PublicKey]);
        var verifier = new ReplayVerifier(authority.ToAuthority());

        Assert.False(verifier.VerifyRun(ticks, result, new StateHasherSimulation(state), checkpoint, registry));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. Checkpoint ordering rules
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckOrdering_StrictlyIncreasing_ReturnsTrue()
    {
        var authority = CreateAuthority();
        var prev = authority.CreateMerkleCheckpoint(1024, CreateHash(60));
        var next = authority.CreateMerkleCheckpoint(2048, CreateHash(61));

        Assert.True(MerkleCheckpoint.CheckOrdering(prev, next));
    }

    [Fact]
    public void CheckOrdering_SameTick_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var prev = authority.CreateMerkleCheckpoint(1024, CreateHash(62));
        var next = authority.CreateMerkleCheckpoint(1024, CreateHash(63));

        Assert.False(MerkleCheckpoint.CheckOrdering(prev, next));
    }

    [Fact]
    public void CheckOrdering_RewindedTick_ReturnsFalse()
    {
        var authority = CreateAuthority();
        var prev = authority.CreateMerkleCheckpoint(2048, CreateHash(64));
        var next = authority.CreateMerkleCheckpoint(1024, CreateHash(65));

        Assert.False(MerkleCheckpoint.CheckOrdering(prev, next));
    }

    [Fact]
    public void CheckOrdering_NullArguments_Throw()
    {
        var authority = CreateAuthority();
        var checkpoint = authority.CreateMerkleCheckpoint(0, CreateHash(66));

        Assert.Throws<ArgumentNullException>(() => MerkleCheckpoint.CheckOrdering(null!, checkpoint));
        Assert.Throws<ArgumentNullException>(() => MerkleCheckpoint.CheckOrdering(checkpoint, null!));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Checkpoint storage – round-trip JSON and MerkleCheckpointStore
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MerkleCheckpoint_ToJsonBytes_FromJsonBytes_RoundTrip()
    {
        var authority = CreateAuthority();
        var original = authority.CreateMerkleCheckpoint(tick: 4096, CreateHash(70));

        var json = original.ToJsonBytes();
        var restored = MerkleCheckpoint.FromJsonBytes(json);

        Assert.Equal(original.Tick, restored.Tick);
        Assert.True(original.MerkleRoot.AsSpan().SequenceEqual(restored.MerkleRoot));
        Assert.True(original.AuthorityPublicKey.AsSpan().SequenceEqual(restored.AuthorityPublicKey));
        Assert.True(original.Signature.AsSpan().SequenceEqual(restored.Signature));
    }

    [Fact]
    public void MerkleCheckpoint_FromJsonBytes_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            MerkleCheckpoint.FromJsonBytes("not valid json"u8.ToArray()));
    }

    [Fact]
    public void MerkleCheckpointStore_SaveAndLoad_RoundTrip()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        try
        {
            var store = new MerkleCheckpointStore(baseDir);
            var authority = CreateAuthority();
            var runId = Guid.NewGuid();
            var checkpoint = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(80));

            store.Save(runId, checkpoint);

            Assert.True(store.Exists(runId, 1024UL));
            var loaded = store.Load(runId, 1024UL);

            Assert.Equal(checkpoint.Tick, loaded.Tick);
            Assert.True(checkpoint.MerkleRoot.AsSpan().SequenceEqual(loaded.MerkleRoot));
            Assert.True(checkpoint.AuthorityPublicKey.AsSpan().SequenceEqual(loaded.AuthorityPublicKey));
            Assert.True(checkpoint.Signature.AsSpan().SequenceEqual(loaded.Signature));
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void MerkleCheckpointStore_Load_MissingFile_ThrowsFileNotFoundException()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        var store = new MerkleCheckpointStore(baseDir);

        Assert.Throws<FileNotFoundException>(() =>
            store.Load(Guid.NewGuid(), tick: 999));
    }

    [Fact]
    public void MerkleCheckpointStore_TryLoad_MissingFile_ReturnsNull()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        var store = new MerkleCheckpointStore(baseDir);

        var result = store.TryLoad(Guid.NewGuid(), tick: 999);

        Assert.Null(result);
    }

    [Fact]
    public void MerkleCheckpointStore_TryLoadAll_MultipleCheckpoints_ReturnsSortedByTick()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        try
        {
            var store = new MerkleCheckpointStore(baseDir);
            var authority = CreateAuthority();
            var runId = Guid.NewGuid();

            store.Save(runId, authority.CreateMerkleCheckpoint(tick: 3072, CreateHash(91)));
            store.Save(runId, authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(92)));
            store.Save(runId, authority.CreateMerkleCheckpoint(tick: 2048, CreateHash(93)));

            var all = store.TryLoadAll(runId);

            Assert.Equal(3, all.Count);
            Assert.Equal(1024UL, all[0].Tick);
            Assert.Equal(2048UL, all[1].Tick);
            Assert.Equal(3072UL, all[2].Tick);
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void MerkleCheckpointStore_TryLoadAll_EmptyDir_ReturnsEmptyList()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        var store = new MerkleCheckpointStore(baseDir);

        var all = store.TryLoadAll(Guid.NewGuid());

        Assert.Empty(all);
    }

    [Fact]
    public void MerkleCheckpointStore_Save_OverwritesExistingFile()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"gunrpg-cp-test-{Guid.NewGuid():N}");
        try
        {
            var store = new MerkleCheckpointStore(baseDir);
            var authority = CreateAuthority();
            var runId = Guid.NewGuid();

            var cp1 = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(100));
            var cp2 = authority.CreateMerkleCheckpoint(tick: 1024, CreateHash(101));

            store.Save(runId, cp1);
            store.Save(runId, cp2);

            var loaded = store.Load(runId, 1024UL);
            Assert.True(loaded.MerkleRoot.AsSpan().SequenceEqual(cp2.MerkleRoot),
                "The file should contain the most recently saved checkpoint.");
        }
        finally
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
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

    private static SessionAuthority CreateAuthority(string id = "signed-checkpoint-test")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
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
    // Minimal IDeterministicSimulation implementations for testing
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class StateHasherSimulation : IDeterministicSimulation
    {
        private readonly SimulationState _state;
        private readonly StateHasher _hasher = new();
        private byte[] _currentHash = new byte[32];
        private long _currentTick = -1;

        public StateHasherSimulation(SimulationState state) => _state = state;

        public void Reset() { _currentHash = new byte[32]; _currentTick = -1; }

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

        public void Reset() { _inner.Reset(); _hasDiverged = false; }

        public void ApplyTick(SignedTick tick)
        {
            _inner.ApplyTick(tick);
            if (tick.Tick >= _divergeAtOrAfterTick)
                _hasDiverged = true;
        }

        public byte[] GetStateHash()
        {
            var hash = _inner.GetStateHash();
            if (_hasDiverged) hash[0] ^= 0xFF;
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
