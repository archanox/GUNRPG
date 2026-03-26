using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the per-tick server-authoritative replay validation system:
/// - TickInput / TickState / SignedTick data structures
/// - TickInputs deterministic multi-player input batching
/// - SessionAuthority.SignTick / VerifyTick (with PrevStateHash chain)
/// - TickAuthorityService checkpoint signing (SIGN_INTERVAL)
/// - Hash-chain integrity (PrevStateHash linkage)
/// - Tick continuity enforcement
/// - VerifyTickChain full-chain validation
/// - DesyncException and InvalidSignatureException detection
/// - NodeRole enum values
/// - SessionAuthority.Sign overload with ReplayHash
/// - SignedRunResult.ReplayHash property
/// - FinalizeRun with chain enforcement
/// </summary>
public sealed class TickAuthorityTests
{
    private static readonly Guid PlayerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Player2Id = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Core data structures
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TickInput_StoresTick_AndInputHash()
    {
        var hash = new byte[32];
        hash[0] = 0xAB;
        var input = new TickInput(42L, hash);

        Assert.Equal(42L, input.Tick);
        Assert.Equal(hash, input.InputHash);
    }

    [Fact]
    public void TickState_StoresTick_AndStateHash()
    {
        var hash = new byte[32];
        hash[0] = 0xCD;
        var state = new TickState(7L, hash);

        Assert.Equal(7L, state.Tick);
        Assert.Equal(hash, state.StateHash);
    }

    [Fact]
    public void SignedTick_StoresAllFields_IncludingPrevStateHash()
    {
        var prevHash = CreateHash(0);
        var stateHash = CreateHash(1);
        var inputHash = CreateHash(2);
        var signature = new byte[64];

        var tick = new SignedTick(5L, prevHash, stateHash, inputHash, signature);

        Assert.Equal(5L, tick.Tick);
        Assert.Equal(prevHash, tick.PrevStateHash);
        Assert.Equal(stateHash, tick.StateHash);
        Assert.Equal(inputHash, tick.InputHash);
        Assert.Equal(signature, tick.Signature);
    }

    [Fact]
    public void NodeRole_HasExpectedValues()
    {
        Assert.Equal(0, (int)NodeRole.Authority);
        Assert.Equal(1, (int)NodeRole.Validator);
        Assert.Equal(2, (int)NodeRole.Client);
    }

    [Fact]
    public void GenesisStateHash_Is32ZeroBytes()
    {
        Assert.Equal(32, TickAuthorityService.GenesisStateHash.Length);
        Assert.All(TickAuthorityService.GenesisStateHash, b => Assert.Equal(0, b));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. TickInputs deterministic multi-player batching
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TickInputs_HashIsDeterministic_ForSameInputs()
    {
        var inputs = CreateSingleInput(0L, PlayerId, new ExfilAction());
        var h1 = inputs.ComputeHash();
        var h2 = inputs.ComputeHash();

        Assert.True(h1.AsSpan().SequenceEqual(h2));
    }

    [Fact]
    public void TickInputs_HashIs32Bytes()
    {
        var inputs = CreateSingleInput(0L, PlayerId, new MoveAction(Direction.North));
        Assert.Equal(32, inputs.ComputeHash().Length);
    }

    [Fact]
    public void TickInputs_DifferentActions_ProduceDifferentHashes()
    {
        var h1 = CreateSingleInput(0L, PlayerId, new MoveAction(Direction.North)).ComputeHash();
        var h2 = CreateSingleInput(0L, PlayerId, new MoveAction(Direction.South)).ComputeHash();
        var h3 = CreateSingleInput(0L, PlayerId, new ExfilAction()).ComputeHash();

        Assert.False(h1.AsSpan().SequenceEqual(h2));
        Assert.False(h1.AsSpan().SequenceEqual(h3));
    }

    [Fact]
    public void TickInputs_MultiPlayer_OrderIsCanonical_RegardlessOfSubmissionOrder()
    {
        var action1 = new MoveAction(Direction.North);
        var action2 = new ExfilAction();

        // Construct with P1 first, then P2
        var inputsAB = new TickInputs(0L, [
            new PlayerInput(PlayerId, action1),
            new PlayerInput(Player2Id, action2)
        ]);

        // Construct with P2 first, then P1 — should produce identical hash
        var inputsBA = new TickInputs(0L, [
            new PlayerInput(Player2Id, action2),
            new PlayerInput(PlayerId, action1)
        ]);

        Assert.True(inputsAB.ComputeHash().AsSpan().SequenceEqual(inputsBA.ComputeHash()));
    }

    [Fact]
    public void TickInputs_DifferentTickNumbers_ProduceDifferentHashes()
    {
        var h1 = CreateSingleInput(0L, PlayerId, new ExfilAction()).ComputeHash();
        var h2 = CreateSingleInput(1L, PlayerId, new ExfilAction()).ComputeHash();

        Assert.False(h1.AsSpan().SequenceEqual(h2));
    }

    [Fact]
    public void HashInputs_MatchesTickInputsComputeHash()
    {
        var inputs = CreateSingleInput(5L, PlayerId, new ExfilAction());
        Assert.True(TickAuthorityService.HashInputs(inputs).AsSpan().SequenceEqual(inputs.ComputeHash()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. SessionAuthority tick signing / verification (with PrevStateHash)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignTick_ProducesValidSignature_VerifiedByVerifyTick()
    {
        var authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);

        var signature = authority.SignTick(0L, prevHash, stateHash, inputHash);
        var signedTick = new SignedTick(0L, prevHash, stateHash, inputHash, signature);

        Assert.True(authority.VerifyTick(signedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenPrevStateHashTampered()
    {
        var authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(0L, prevHash, stateHash, inputHash);

        // Tamper prevStateHash
        var tamperedTick = new SignedTick(0L, CreateHash(99), stateHash, inputHash, signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenStateHashTampered()
    {
        var authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, prevHash, stateHash, inputHash);

        var tamperedTick = new SignedTick(5L, prevHash, CreateHash(99), inputHash, signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenInputHashTampered()
    {
        var authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, prevHash, stateHash, inputHash);

        var tamperedTick = new SignedTick(5L, prevHash, stateHash, CreateHash(99), signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenTickNumberTampered()
    {
        var authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, prevHash, stateHash, inputHash);

        var tamperedTick = new SignedTick(99L, prevHash, stateHash, inputHash, signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenSignatureFromDifferentAuthority()
    {
        var authorityA = CreateAuthority("auth-a");
        var authorityB = CreateAuthority("auth-b");
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);

        var sigA = authorityA.SignTick(0L, prevHash, stateHash, inputHash);
        var tickSignedByA = new SignedTick(0L, prevHash, stateHash, inputHash, sigA);

        Assert.True(authorityA.VerifyTick(tickSignedByA));
        Assert.False(authorityB.VerifyTick(tickSignedByA));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. TickAuthorityService checkpoint signing (SIGN_INTERVAL = 10)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignInterval_IsCorrectValue()
    {
        Assert.Equal(10, TickAuthorityService.SignInterval);
    }

    [Fact]
    public void ProcessTick_ProducesSignedTick_AtCheckpointTicks()
    {
        var service = CreateService();
        var state = ReplayRunner.CreateInitialState(42);
        var prev = TickAuthorityService.GenesisStateHash;

        // Tick 0 (0 % 10 == 0)
        var (ts0, st0) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), prev);
        Assert.NotNull(st0);
        Assert.Equal(0L, ts0.Tick);
        Assert.Equal(0L, st0.Tick);

        // Tick 10 (10 % 10 == 0)
        var (_, st10) = service.ProcessTick(10L, state, PlayerId, new ExfilAction(), prev);
        Assert.NotNull(st10);
        Assert.Equal(10L, st10.Tick);
    }

    [Fact]
    public void ProcessTick_DoesNotSignBetweenCheckpoints()
    {
        var service = CreateService();
        var state = ReplayRunner.CreateInitialState(42);
        var prev = TickAuthorityService.GenesisStateHash;

        foreach (var tick in new long[] { 1L, 2L, 3L, 5L, 7L, 9L })
        {
            var (_, signedTick) = service.ProcessTick(tick, state, PlayerId, new ExfilAction(), prev);
            Assert.Null(signedTick);
        }
    }

    [Fact]
    public void ProcessTick_SignedTickSignature_VerifiesCorrectly()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var prev = TickAuthorityService.GenesisStateHash;

        var (_, signedTick) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), prev);

        Assert.NotNull(signedTick);
        Assert.True(authority.VerifyTick(signedTick));
    }

    [Fact]
    public void ProcessTick_SignedTick_HasCorrectPrevStateHash_ForGenesis()
    {
        var service = CreateService();
        var state = ReplayRunner.CreateInitialState(42);

        var (_, signedTick) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(),
            TickAuthorityService.GenesisStateHash);

        Assert.NotNull(signedTick);
        Assert.True(signedTick.PrevStateHash.AsSpan().SequenceEqual(TickAuthorityService.GenesisStateHash));
    }

    [Fact]
    public void ProcessTick_HashChaining_PrevStateHashLinkedToLastSignedTick()
    {
        var service = CreateService();
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        // First checkpoint at tick 0
        var (_, tick0) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(tick0);

        // Second checkpoint at tick 10 — prevSignedStateHash must be tick0.StateHash
        var (_, tick10) = service.ProcessTick(10L, state, PlayerId, new ExfilAction(), tick0.StateHash);
        Assert.NotNull(tick10);

        // The hash chain must link tick10 to tick0
        Assert.True(tick10.PrevStateHash.AsSpan().SequenceEqual(tick0.StateHash));
    }

    [Fact]
    public void ProcessTick_StateHash_IsConsistentWithDirectHasher()
    {
        var hasher = new StateHasher();
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority, hasher);
        var state = ReplayRunner.CreateInitialState(99);

        var (tickState, _) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(),
            TickAuthorityService.GenesisStateHash);
        var directHash = hasher.HashTick(0L, state);

        Assert.True(tickState.StateHash.AsSpan().SequenceEqual(directHash));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. VerifySignedTickOrThrow: desync, invalid-signature, continuity
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySignedTickOrThrow_PassesForValidSignedTickAndMatchingLocalHash()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (tickState, signedTick) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);

        Assert.NotNull(signedTick);
        // Should not throw
        service.VerifySignedTickOrThrow(signedTick, tickState.StateHash);
    }

    [Fact]
    public void VerifySignedTickOrThrow_ThrowsInvalidSignatureException_ForTamperedSignature()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (tickState, signedTick) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(signedTick);

        var tamperedTick = new SignedTick(
            signedTick.Tick,
            signedTick.PrevStateHash,
            CreateHash(99),  // wrong state hash → signature fails
            signedTick.InputHash,
            signedTick.Signature);

        var ex = Assert.Throws<InvalidSignatureException>(
            () => service.VerifySignedTickOrThrow(tamperedTick, tickState.StateHash));
        Assert.Equal(0L, ex.Tick);
    }

    [Fact]
    public void VerifySignedTickOrThrow_ThrowsDesyncException_WhenLocalHashDiffers()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (tickState, signedTick) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(signedTick);

        var wrongLocalHash = CreateHash(99);
        var ex = Assert.Throws<DesyncException>(
            () => service.VerifySignedTickOrThrow(signedTick, wrongLocalHash));

        Assert.Equal(0L, ex.Tick);
        Assert.True(tickState.StateHash.AsSpan().SequenceEqual(ex.ExpectedHash));
        Assert.True(wrongLocalHash.AsSpan().SequenceEqual(ex.ActualHash));
    }

    [Fact]
    public void VerifySignedTickOrThrow_ThrowsArgumentException_OnTickContinuityViolation()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (ts0, tick0) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(tick0);

        // tick 20 instead of tick 1 — violates continuity
        var (ts20, tick20) = service.ProcessTick(20L, state, PlayerId, new ExfilAction(), tick0.StateHash);
        Assert.NotNull(tick20);

        var ex = Assert.Throws<ArgumentException>(
            () => service.VerifySignedTickOrThrow(tick20, ts20.StateHash, tick0));
        Assert.Contains("20", ex.Message);
    }

    [Fact]
    public void VerifySignedTickOrThrow_ThrowsDesyncException_WhenHashChainBroken()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (ts0, tick0) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(tick0);

        // Sign tick 1 with a *wrong* prevSignedStateHash (not tick0.StateHash)
        var wrongPrev = CreateHash(99);
        var tick1StateHash = new StateHasher().HashTick(1L, state);
        var tick1InputHash = CreateSingleInput(1L, PlayerId, new ExfilAction()).ComputeHash();
        var tick1Sig = authority.SignTick(1L, wrongPrev, tick1StateHash, tick1InputHash);
        var tick1WithWrongChain = new SignedTick(1L, wrongPrev, tick1StateHash, tick1InputHash, tick1Sig);

        // Signature is valid but chain is broken
        Assert.True(authority.VerifyTick(tick1WithWrongChain));
        Assert.Throws<DesyncException>(
            () => service.VerifySignedTickOrThrow(tick1WithWrongChain, tick1StateHash, tick0));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. VerifyTickChain
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifyTickChain_PassesForValidSequentialChain()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);

        // Should not throw
        service.VerifyTickChain(ticks);
    }

    [Fact]
    public void VerifyTickChain_FailsWhenGenesisPrevHashNotAllZero()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        // Sign tick 0 with a non-genesis prevHash
        var wrongGenesisPrev = CreateHash(1);
        var stateHash = new StateHasher().HashTick(0L, state);
        var inputHash = CreateSingleInput(0L, PlayerId, new ExfilAction()).ComputeHash();
        var sig = authority.SignTick(0L, wrongGenesisPrev, stateHash, inputHash);
        var tick0Bad = new SignedTick(0L, wrongGenesisPrev, stateHash, inputHash, sig);

        Assert.Throws<DesyncException>(() => service.VerifyTickChain([tick0Bad]));
    }

    [Fact]
    public void VerifyTickChain_PassesForNonSequentialCheckpoints_WithCorrectSpacing()
    {
        // VerifyTickChain allows any strictly-increasing spacing between checkpoints
        // (e.g. 0 → 10 → 20), not just +1, matching the SignInterval checkpoint model.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var hasher = new StateHasher();
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        // Manually build checkpoints at tick 0 and tick 20 (skip 10)
        var stateHash0 = hasher.HashTick(0L, state);
        var inputHash0 = CreateSingleInput(0L, PlayerId, new ExfilAction()).ComputeHash();
        var sig0 = authority.SignTick(0L, genesis, stateHash0, inputHash0);
        var tick0 = new SignedTick(0L, genesis, stateHash0, inputHash0, sig0);

        var stateHash20 = hasher.HashTick(20L, state);
        var inputHash20 = CreateSingleInput(20L, PlayerId, new ExfilAction()).ComputeHash();
        var sig20 = authority.SignTick(20L, tick0.StateHash, stateHash20, inputHash20);
        var tick20 = new SignedTick(20L, tick0.StateHash, stateHash20, inputHash20, sig20);

        // Should NOT throw — 20 > 0 is strictly increasing
        service.VerifyTickChain([tick0, tick20]);
    }

    [Fact]
    public void VerifyTickChain_FailsOnNonIncreasingTicks()
    {
        // VerifyTickChain must reject duplicate or decreasing tick indices.
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var hasher = new StateHasher();
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        // tick 0
        var stateHash0 = hasher.HashTick(0L, state);
        var inputHash0 = CreateSingleInput(0L, PlayerId, new ExfilAction()).ComputeHash();
        var sig0 = authority.SignTick(0L, genesis, stateHash0, inputHash0);
        var tick0 = new SignedTick(0L, genesis, stateHash0, inputHash0, sig0);

        // tick 0 again (non-increasing: 0 is not > 0)
        var sig0Again = authority.SignTick(0L, stateHash0, stateHash0, inputHash0);
        var tick0Again = new SignedTick(0L, stateHash0, stateHash0, inputHash0, sig0Again);

        Assert.Throws<ArgumentException>(() => service.VerifyTickChain([tick0, tick0Again]));
    }

    [Fact]
    public void VerifyTickChain_FailsOnHashChainBreak()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);
        var genesis = TickAuthorityService.GenesisStateHash;

        var (_, tick0) = service.ProcessTick(0L, state, PlayerId, new ExfilAction(), genesis);
        Assert.NotNull(tick0);

        // tick1 references wrong prev hash
        var wrongPrev = CreateHash(99);
        var stateHash1 = new StateHasher().HashTick(1L, state);
        var inputHash1 = CreateSingleInput(1L, PlayerId, new ExfilAction()).ComputeHash();
        var sig1 = authority.SignTick(1L, wrongPrev, stateHash1, inputHash1);
        var tick1Bad = new SignedTick(1L, wrongPrev, stateHash1, inputHash1, sig1);

        Assert.Throws<DesyncException>(() => service.VerifyTickChain([tick0, tick1Bad]));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. FinalizeRun with chain enforcement
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_WithVerifiedChain_ProducesValidSignedResult()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 1);
        var finalStateHash = ticks[^1].StateHash;
        var replayHash = CreateHash(50);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalStateHash, replayHash, ticks);

        Assert.NotNull(result.ReplayHash);
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void FinalizeRun_WithVerifiedChain_ThrowsWhenFinalHashMismatch()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 1);
        var wrongFinalHash = CreateHash(99);
        var replayHash = CreateHash(50);

        Assert.Throws<InvalidOperationException>(
            () => service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), wrongFinalHash, replayHash, ticks));
    }

    [Fact]
    public void FinalizeRun_WithReplayHash_SignatureVerifies()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var finalHash = CreateHash(10);
        var replayHash = CreateHash(11);

        var result = service.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), finalHash, replayHash);

        Assert.NotNull(result.ReplayHash);
        Assert.Equal(Convert.ToHexString(finalHash), result.FinalHash);
        Assert.Equal(Convert.ToHexString(replayHash), result.ReplayHash);
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. SessionAuthority.Sign overload with ReplayHash
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignWithReplayHash_ProducesResult_WithReplayHashSet()
    {
        var authority = CreateAuthority();
        var finalHash = CreateHash(1);
        var replayHash = CreateHash(2);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var result = authority.Sign(sessionId, playerId, finalHash, replayHash);

        Assert.NotNull(result.ReplayHash);
        Assert.Equal(Convert.ToHexString(replayHash), result.ReplayHash);
        Assert.Equal(Convert.ToHexString(finalHash), result.FinalHash);
    }

    [Fact]
    public void SignWithReplayHash_SignatureVerifies()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(3), CreateHash(4));
        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void SignedRunResult_WithoutReplayHash_HasNullReplayHash()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1));
        Assert.Null(result.ReplayHash);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 9. Exception types
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DesyncException_ExposesCorrectProperties()
    {
        var expected = CreateHash(1);
        var actual = CreateHash(2);
        var ex = new DesyncException(5L, expected, actual);

        Assert.Equal(5L, ex.Tick);
        Assert.True(expected.AsSpan().SequenceEqual(ex.ExpectedHash));
        Assert.True(actual.AsSpan().SequenceEqual(ex.ActualHash));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void InvalidSignatureException_ExposesCorrectTick()
    {
        var ex = new InvalidSignatureException(99L);

        Assert.Equal(99L, ex.Tick);
        Assert.Contains("99", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 10. ISessionAuthority interface compliance and ITickVerifier separation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionAuthority_ImplementsISessionAuthority()
    {
        var authority = CreateAuthority();
        Assert.IsAssignableFrom<ISessionAuthority>(authority);
    }

    [Fact]
    public void SessionAuthority_ImplementsITickVerifier()
    {
        var authority = CreateAuthority();
        Assert.IsAssignableFrom<ITickVerifier>(authority);
    }

    [Fact]
    public void ISessionAuthority_SignAndVerify_WorksThroughInterface()
    {
        ISessionAuthority authority = CreateAuthority();
        var prevHash = TickAuthorityService.GenesisStateHash;
        var stateHash = CreateHash(1);
        var inputHash = CreateHash(2);

        var signature = authority.SignTick(0L, prevHash, stateHash, inputHash);
        var tick = new SignedTick(0L, prevHash, stateHash, inputHash, signature);

        Assert.True(authority.VerifyTick(tick));
    }

    [Fact]
    public void VerifyTickChain_WorksWithITickVerifier_VerifierOnlyConstructor()
    {
        // Validator/client nodes should be able to verify a chain with only the public key.
        var authority = CreateAuthority();
        ITickVerifier verifier = authority; // only public key side

        var verifierService = new TickAuthorityService(verifier);
        var state = ReplayRunner.CreateInitialState(42);
        var ticks = BuildSignedChain(authority, state, startTick: 0, count: 3);

        // Should not throw — verifier-only service can validate a chain
        verifierService.VerifyTickChain(ticks);
    }

    [Fact]
    public void FinalizeRun_ThrowsInvalidOperationException_ForVerifierOnlyService()
    {
        var authority = CreateAuthority();
        ITickVerifier verifier = authority;
        var verifierService = new TickAuthorityService(verifier);

        Assert.Throws<InvalidOperationException>(
            () => verifierService.FinalizeRun(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1), CreateHash(2)));
    }

    [Fact]
    public void ProcessTick_ThrowsInvalidOperationException_ForVerifierOnlyService()
    {
        var authority = CreateAuthority();
        ITickVerifier verifier = authority;
        var verifierService = new TickAuthorityService(verifier);
        var state = ReplayRunner.CreateInitialState(42);

        // Checkpoint tick (0 % 10 == 0) triggers signing — should throw since no private key
        Assert.Throws<InvalidOperationException>(
            () => verifierService.ProcessTick(0L, state, PlayerId, new ExfilAction(),
                TickAuthorityService.GenesisStateHash));
    }

    [Fact]
    public void GenesisStateHash_ReturnsNewCopyEachCall_PreventsMutation()
    {
        var h1 = TickAuthorityService.GenesisStateHash;
        var h2 = TickAuthorityService.GenesisStateHash;

        // Must be equal (both all-zero, 32 bytes) but not the same reference
        Assert.Equal(32, h1.Length);
        Assert.True(h1.AsSpan().SequenceEqual(h2));
        Assert.NotSame(h1, h2);

        // Mutating one copy must not affect the next call
        h1[0] = 0xFF;
        var h3 = TickAuthorityService.GenesisStateHash;
        Assert.Equal(0, h3[0]);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Determinism: same seed + same inputs → identical tick hashes on every run
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TickAuthority_SameSeedAndInputs_ProduceIdenticalStateHashes_AcrossRuns()
    {
        // Arrange: fixed seed and a deterministic input sequence.
        const int seed = 777;
        PlayerAction[] actions =
        [
            new MoveAction(Direction.North),
            new AttackAction(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new UseItemAction(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new ExfilAction()
        ];

        // Act: run the same simulation twice with an identical authority key and inputs.
        var authority = CreateAuthority();
        var serviceA = new TickAuthorityService(authority);
        var serviceB = new TickAuthorityService(authority);

        var hashesA = RunSimulationTicks(serviceA, seed, actions);
        var hashesB = RunSimulationTicks(serviceB, seed, actions);

        // Assert: every per-tick state hash must be bit-for-bit identical.
        Assert.Equal(hashesA.Count, hashesB.Count);
        for (var i = 0; i < hashesA.Count; i++)
        {
            Assert.True(
                hashesA[i].AsSpan().SequenceEqual(hashesB[i]),
                $"State hash diverged at tick {i}.");
        }
    }

    [Fact]
    public void TickAuthority_ReplayProducesSameStateHashes_AsOriginalRun()
    {
        // Arrange: a fixed seed and input log that exercises multiple simulation paths.
        const int seed = 12345;
        var input = new RunInput
        {
            RunId = Guid.Parse("deadbeef-dead-beef-dead-beefdeadbeef"),
            PlayerId = PlayerId,
            Seed = seed,
            Actions =
            [
                new MoveAction(Direction.North),
                new AttackAction(Guid.Parse("33333333-3333-3333-3333-333333333333")),
                new UseItemAction(Guid.Parse("44444444-4444-4444-4444-444444444444")),
                new ExfilAction()
            ]
        };

        var log = InputLog.FromRunInput(input);

        // Original run
        var runner = new ReplayRunner();
        var originalResult = runner.Replay(log);

        // Replay validation: must reproduce the exact same per-tick hashes.
        var replayResult = runner.ValidateReplay(log, originalResult.TickHashes);

        Assert.True(
            originalResult.FinalHash.AsSpan().SequenceEqual(replayResult.FinalHash),
            "Replay final hash must equal the original run's final hash.");

        for (var i = 0; i < originalResult.TickHashes.Count; i++)
        {
            Assert.True(
                originalResult.TickHashes[i].AsSpan().SequenceEqual(replayResult.TickHashes[i]),
                $"Per-tick hash diverged at index {i}.");
        }
    }

    /// <summary>
    /// Runs a sequence of simulation ticks through a <see cref="TickAuthorityService"/> and
    /// returns the per-tick state hashes produced by <see cref="StateHasher"/>.
    /// </summary>
    private static List<byte[]> RunSimulationTicks(
        TickAuthorityService service,
        int seed,
        PlayerAction[] actions)
    {
        var hasher = new StateHasher();
        var state = ReplayRunner.CreateInitialState(seed);
        var prevSignedHash = TickAuthorityService.GenesisStateHash;
        var tickHashes = new List<byte[]>(actions.Length);

        for (var i = 0; i < actions.Length; i++)
        {
            var tick = (long)i;
            state = Simulation.Step(state, actions[i], tick);
            var stateHash = hasher.HashTick(tick, state);
            tickHashes.Add(stateHash);

            var (_, signedTick) = service.ProcessTick(tick, state, PlayerId, actions[i], prevSignedHash);
            if (signedTick is not null)
            {
                prevSignedHash = signedTick.StateHash;
            }
        }

        return tickHashes;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SessionAuthority CreateAuthority(string id = "test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static TickAuthorityService CreateService()
        => new(CreateAuthority());

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xAB;
        return hash;
    }

    private static TickInputs CreateSingleInput(long tick, Guid playerId, PlayerAction action)
        => new(tick, [new PlayerInput(playerId, action)]);

    /// <summary>
    /// Builds a chain of sequential signed ticks starting at <paramref name="startTick"/>.
    /// Ticks are signed directly (not via SIGN_INTERVAL) so the chain is purely sequential.
    /// </summary>
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
            var tick = startTick + i;
            var stateHash = hasher.HashTick(tick, state);
            var inputHash = new TickInputs(tick, [new PlayerInput(PlayerId, new ExfilAction())]).ComputeHash();
            var sig = authority.SignTick(tick, prev, stateHash, inputHash);
            var signedTick = new SignedTick(tick, prev, stateHash, inputHash, sig);
            ticks.Add(signedTick);
            prev = stateHash;
        }

        return ticks;
    }
}
