using GUNRPG.Core.Simulation;
using GUNRPG.Security;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the per-tick server-authoritative replay validation system:
/// - TickInput / TickState / SignedTick data structures
/// - SessionAuthority.SignTick / VerifyTick
/// - TickAuthorityService checkpoint signing (SIGN_INTERVAL)
/// - DesyncException and InvalidSignatureException detection
/// - NodeRole enum values
/// - SessionAuthority.Sign overload with ReplayHash
/// - SignedRunResult.ReplayHash property
/// </summary>
public sealed class TickAuthorityTests
{
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
    public void SignedTick_StoresAllFields()
    {
        var stateHash = CreateHash(1);
        var inputHash = CreateHash(2);
        var signature = new byte[64];

        var tick = new SignedTick(5L, stateHash, inputHash, signature);

        Assert.Equal(5L, tick.Tick);
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

    // ──────────────────────────────────────────────────────────────────────────
    // 2. SessionAuthority tick signing / verification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignTick_ProducesValidSignature_VerifiedByVerifyTick()
    {
        var authority = CreateAuthority();
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);

        var signature = authority.SignTick(0L, stateHash, inputHash);
        var signedTick = new SignedTick(0L, stateHash, inputHash, signature);

        Assert.True(authority.VerifyTick(signedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenStateHashTampered()
    {
        var authority = CreateAuthority();
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, stateHash, inputHash);

        // Tamper state hash
        var tamperedTick = new SignedTick(5L, CreateHash(99), inputHash, signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenInputHashTampered()
    {
        var authority = CreateAuthority();
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, stateHash, inputHash);

        // Tamper input hash
        var tamperedTick = new SignedTick(5L, stateHash, CreateHash(99), signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenTickNumberTampered()
    {
        var authority = CreateAuthority();
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);
        var signature = authority.SignTick(5L, stateHash, inputHash);

        // Tamper tick number
        var tamperedTick = new SignedTick(99L, stateHash, inputHash, signature);

        Assert.False(authority.VerifyTick(tamperedTick));
    }

    [Fact]
    public void VerifyTick_ReturnsFalse_WhenSignatureFromDifferentAuthority()
    {
        var authorityA = CreateAuthority("auth-a");
        var authorityB = CreateAuthority("auth-b");
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);

        var signatureFromA = authorityA.SignTick(0L, stateHash, inputHash);
        var tickSignedByA = new SignedTick(0L, stateHash, inputHash, signatureFromA);

        Assert.True(authorityA.VerifyTick(tickSignedByA));
        Assert.False(authorityB.VerifyTick(tickSignedByA));
    }

    [Fact]
    public void SignTick_IsDeterministic_SameTick_SameInputs()
    {
        // Ed25519 signatures are deterministic (RFC 8032)
        var authority = CreateAuthority();
        var stateHash = CreateHash(10);
        var inputHash = CreateHash(20);

        var sig1 = authority.SignTick(3L, stateHash, inputHash);
        var sig2 = authority.SignTick(3L, stateHash, inputHash);

        Assert.True(sig1.AsSpan().SequenceEqual(sig2));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. TickAuthorityService checkpoint signing (SIGN_INTERVAL = 10)
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
        var action = new ExfilAction();

        // Tick 0 is a checkpoint (0 % 10 == 0)
        var (tickState0, signedTick0) = service.ProcessTick(0L, state, action);
        Assert.NotNull(signedTick0);
        Assert.Equal(0L, tickState0.Tick);
        Assert.Equal(0L, signedTick0.Tick);

        // Tick 10 is a checkpoint
        var (_, signedTick10) = service.ProcessTick(10L, state, action);
        Assert.NotNull(signedTick10);
        Assert.Equal(10L, signedTick10.Tick);
    }

    [Fact]
    public void ProcessTick_DoesNotSignBetweenCheckpoints()
    {
        var service = CreateService();
        var state = ReplayRunner.CreateInitialState(42);
        var action = new ExfilAction();

        foreach (var tick in new long[] { 1L, 2L, 3L, 5L, 7L, 9L })
        {
            var (_, signedTick) = service.ProcessTick(tick, state, action);
            Assert.Null(signedTick);
        }
    }

    [Fact]
    public void ProcessTick_SignedTickSignature_VerifiesCorrectly()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var (_, signedTick) = service.ProcessTick(0L, state, new ExfilAction());

        Assert.NotNull(signedTick);
        Assert.True(authority.VerifyTick(signedTick));
    }

    [Fact]
    public void ProcessTick_StateHash_IsConsistentWithDirectHasher()
    {
        var hasher = new StateHasher();
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority, hasher);
        var state = ReplayRunner.CreateInitialState(99);

        var (tickState, _) = service.ProcessTick(0L, state, new ExfilAction());
        var directHash = hasher.HashTick(0L, state);

        Assert.True(tickState.StateHash.AsSpan().SequenceEqual(directHash));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. VerifySignedTickOrThrow: desync and invalid-signature detection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void VerifySignedTickOrThrow_PassesForValidSignedTickAndMatchingLocalHash()
    {
        var authority = CreateAuthority();
        var service = new TickAuthorityService(authority);
        var state = ReplayRunner.CreateInitialState(42);

        var (tickState, signedTick) = service.ProcessTick(0L, state, new ExfilAction());

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

        var (tickState, signedTick) = service.ProcessTick(0L, state, new ExfilAction());
        Assert.NotNull(signedTick);

        // Tamper the state hash in the signed tick
        var tamperedTick = new SignedTick(
            signedTick.Tick,
            CreateHash(99), // wrong hash → signature fails
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

        var (tickState, signedTick) = service.ProcessTick(0L, state, new ExfilAction());
        Assert.NotNull(signedTick);

        // Local state differs from the signed tick's state hash
        var wrongLocalHash = CreateHash(99);

        var ex = Assert.Throws<DesyncException>(
            () => service.VerifySignedTickOrThrow(signedTick, wrongLocalHash));

        Assert.Equal(0L, ex.Tick);
        Assert.True(tickState.StateHash.AsSpan().SequenceEqual(ex.ExpectedHash));
        Assert.True(wrongLocalHash.AsSpan().SequenceEqual(ex.ActualHash));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. SessionAuthority.Sign overload with ReplayHash
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
        var finalHash = CreateHash(3);
        var replayHash = CreateHash(4);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var result = authority.Sign(sessionId, playerId, finalHash, replayHash);

        Assert.True(SessionAuthority.VerifySignedRun(result, authority.ToAuthority()));
    }

    [Fact]
    public void SignWithReplayHash_DifferentFromSignWithoutReplayHash()
    {
        // The two Sign overloads use different payload hash functions, so signatures differ.
        var authority = CreateAuthority();
        var finalHash = CreateHash(5);
        var replayHash = CreateHash(6);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var withReplay = authority.Sign(sessionId, playerId, finalHash, replayHash);
        var withoutReplay = authority.Sign(sessionId, playerId, finalHash);

        Assert.False(withReplay.Signature.AsSpan().SequenceEqual(withoutReplay.Signature));
    }

    [Fact]
    public void VerifySignedRun_RejectsWithoutReplay_ResultThatWasSignedWithReplay()
    {
        // A result signed with replay hash must NOT verify against the old payload function.
        var authority = CreateAuthority();
        var finalHash = CreateHash(7);
        var replayHash = CreateHash(8);
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var withReplay = authority.Sign(sessionId, playerId, finalHash, replayHash);

        // Build an equivalent result WITHOUT the replay hash field (simulating an old result)
        var withoutReplayField = new SignedRunResult(
            withReplay.SessionId,
            withReplay.PlayerId,
            withReplay.FinalHash,
            withReplay.AuthorityId,
            withReplay.Signature,
            replayHash: null);

        // VerifySignedRun on a result without ReplayHash uses the old payload.
        // The signature was computed with the new payload so verification must fail.
        Assert.False(SessionAuthority.VerifySignedRun(withoutReplayField, authority.ToAuthority()));
    }

    [Fact]
    public void SignedRunResult_WithoutReplayHash_HasNullReplayHash()
    {
        var authority = CreateAuthority();
        var result = authority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(1));

        Assert.Null(result.ReplayHash);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. TickAuthorityService.FinalizeRun with combined hashes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalizeRun_ProducesSignedResultWithBothHashes()
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
    // 7. TickAuthorityService.HashAction determinism
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HashAction_IsDeterministic_ForSameAction()
    {
        var action = new MoveAction(Direction.North);

        var hash1 = TickAuthorityService.HashAction(action);
        var hash2 = TickAuthorityService.HashAction(action);

        Assert.True(hash1.AsSpan().SequenceEqual(hash2));
    }

    [Fact]
    public void HashAction_DifferentActions_ProduceDifferentHashes()
    {
        var h1 = TickAuthorityService.HashAction(new MoveAction(Direction.North));
        var h2 = TickAuthorityService.HashAction(new MoveAction(Direction.South));
        var h3 = TickAuthorityService.HashAction(new ExfilAction());

        Assert.False(h1.AsSpan().SequenceEqual(h2));
        Assert.False(h1.AsSpan().SequenceEqual(h3));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Exception types
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
    // 9. ISessionAuthority interface compliance
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SessionAuthority_ImplementsISessionAuthority()
    {
        var authority = CreateAuthority();

        Assert.IsAssignableFrom<ISessionAuthority>(authority);
    }

    [Fact]
    public void ISessionAuthority_SignAndVerify_WorksThroughInterface()
    {
        ISessionAuthority authority = CreateAuthority();
        var stateHash = CreateHash(1);
        var inputHash = CreateHash(2);

        var signature = authority.SignTick(0L, stateHash, inputHash);
        var tick = new SignedTick(0L, stateHash, inputHash, signature);

        Assert.True(authority.VerifyTick(tick));
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
    {
        return new TickAuthorityService(CreateAuthority());
    }

    private static byte[] CreateHash(int seed)
    {
        var hash = new byte[32];
        hash[0] = (byte)(seed & 0xFF);
        hash[1] = (byte)((seed >> 8) & 0xFF);
        hash[15] = 0xAB;
        return hash;
    }
}
