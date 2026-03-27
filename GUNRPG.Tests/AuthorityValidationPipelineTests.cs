using GUNRPG.Application.Combat;
using GUNRPG.Application.Distributed;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Intents;
using GUNRPG.Security;

namespace GUNRPG.Tests;

/// <summary>
/// Tests the end-to-end authority validation pipeline:
/// SignedRunResult creation → signature verification → replay-based tamper detection.
/// </summary>
public sealed class AuthorityValidationPipelineTests
{
    // ──────────────────────────────────────────────────────────────────────
    // 1. Valid run → signature verifies
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidRun_SignatureVerifies()
    {
        var sessionAuthority = CreateSessionAuthority();
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var finalHashBytes = CreateHash(1);

        var signed = sessionAuthority.Sign(sessionId, playerId, finalHashBytes);

        Assert.Equal(sessionId, signed.SessionId);
        Assert.Equal(playerId, signed.PlayerId);
        Assert.Equal(sessionAuthority.Id, signed.AuthorityId);
        Assert.True(SessionAuthority.VerifySignedRun(signed, sessionAuthority.ToAuthority()));
    }

    [Fact]
    public void ValidRun_SignedFromSession_VerifiesWithReplayPipeline()
    {
        // Create a session, capture its initial snapshot, then sign the replay-derived hash.
        var session = CombatSession.CreateDefault(seed: 42);
        var initialJson = OfflineCombatReplay.SerializeCombatSnapshot(SessionMapping.ToSnapshot(session));

        // Replay with no turns to get the authoritative final hash.
        var replayResult = ReplayRunner.Run(initialJson, Array.Empty<IntentSnapshot>());
        var finalHashBytes = CombatSessionHasher.ComputeStateHash(replayResult.Snapshot);

        var sessionAuthority = CreateSessionAuthority();
        var playerId = session.OperatorId.Value;
        var signed = sessionAuthority.Sign(session.Id, playerId, finalHashBytes);

        // Full pipeline: replay → hash → compare → verify signature.
        Assert.True(SessionAuthority.VerifySignedRunWithReplay(
            signed,
            sessionAuthority.ToAuthority(),
            initialJson,
            Array.Empty<IntentSnapshot>()));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2. Tampered hash → signature fails
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TamperedHash_SignatureFails()
    {
        var sessionAuthority = CreateSessionAuthority();
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        var originalHashBytes = CreateHash(2);

        var signed = sessionAuthority.Sign(sessionId, playerId, originalHashBytes);

        // Build a new SignedRunResult with a tampered FinalHash but the same signature.
        var tamperedHashBytes = CreateHash(99);
        var tamperedHex = Convert.ToHexString(tamperedHashBytes);
        var tampered = new SignedRunResult(
            signed.SessionId,
            signed.PlayerId,
            tamperedHex,
            signed.AuthorityId,
            signed.Signature);

        Assert.False(SessionAuthority.VerifySignedRun(tampered, sessionAuthority.ToAuthority()));
    }

    [Fact]
    public void TamperedSignatureBytes_VerificationFails()
    {
        var sessionAuthority = CreateSessionAuthority();
        var signed = sessionAuthority.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(3));

        // Flip the first byte of the returned signature copy to create a corrupted version.
        var modifiedSig = signed.Signature;
        modifiedSig[0] ^= 0xFF;

        var tampered = new SignedRunResult(
            signed.SessionId,
            signed.PlayerId,
            signed.FinalHash,
            signed.AuthorityId,
            modifiedSig);

        Assert.False(SessionAuthority.VerifySignedRun(tampered, sessionAuthority.ToAuthority()));
    }

    [Fact]
    public void WrongAuthority_VerificationFails()
    {
        var authorityA = CreateSessionAuthority("authority-A");
        var authorityB = CreateSessionAuthority("authority-B");

        var signed = authorityA.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(4));

        // Trying to verify with authority-B should fail both on the Id check.
        Assert.False(SessionAuthority.VerifySignedRun(signed, authorityB.ToAuthority()));
    }

    [Fact]
    public void WrongAuthorityPublicKey_SameId_VerificationFails()
    {
        // Two authorities with the same string ID but different keys.
        var keyA = SessionAuthority.GeneratePrivateKey();
        var keyB = SessionAuthority.GeneratePrivateKey();
        const string sharedId = "shared-authority";
        var authorityA = new SessionAuthority(keyA, sharedId);
        var authorityB = new SessionAuthority(keyB, sharedId);

        var signed = authorityA.Sign(Guid.NewGuid(), Guid.NewGuid(), CreateHash(5));

        // authorityB has the same Id but a different public key → signature invalid.
        Assert.False(SessionAuthority.VerifySignedRun(signed, authorityB.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 3. Tampered replay → replay mismatch detected
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TamperedReplay_ReplayMismatchDetected()
    {
        // Build a session with a recorded initial snapshot.
        var session = CombatSession.CreateDefault(seed: 77);
        var initialJson = OfflineCombatReplay.SerializeCombatSnapshot(SessionMapping.ToSnapshot(session));

        // Replay with no turns → compute the deterministic hash of the initial state.
        var replayResult = ReplayRunner.Run(initialJson, Array.Empty<IntentSnapshot>());
        var realFinalHashBytes = CombatSessionHasher.ComputeStateHash(replayResult.Snapshot);

        var sessionAuthority = CreateSessionAuthority();
        var playerId = session.OperatorId.Value;
        var signed = sessionAuthority.Sign(session.Id, playerId, realFinalHashBytes);

        // Verify with the correct (empty) replay turns → should pass.
        Assert.True(SessionAuthority.VerifySignedRunWithReplay(
            signed,
            sessionAuthority.ToAuthority(),
            initialJson,
            Array.Empty<IntentSnapshot>()));

        // Inject a fake turn so the replayed hash differs from the signed hash.
        var fakeTurn = new IntentSnapshot
        {
            OperatorId = session.OperatorId.Value,
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand,
            Stance = StanceAction.None,
            Cover = CoverAction.None,
            SubmittedAtMs = 1000
        };

        Assert.False(SessionAuthority.VerifySignedRunWithReplay(
            signed,
            sessionAuthority.ToAuthority(),
            initialJson,
            new[] { fakeTurn }));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 4. Different nodes → same replay → same hash
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DifferentNodes_SameReplay_SameHash()
    {
        var nodeId1 = Guid.NewGuid();
        var nodeId2 = Guid.NewGuid();
        var engine = new DefaultGameEngine();

        var authority1 = new ReplayGameAuthority(nodeId1, engine);
        var authority2 = new ReplayGameAuthority(nodeId2, engine);

        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();
        var actions = new[]
        {
            AuthorityActionFactory.CreateReplayBackedAction(operatorA, PrimaryAction.Fire),
            AuthorityActionFactory.CreateReplayBackedAction(operatorB, PrimaryAction.Reload),
        };

        foreach (var action in actions)
        {
            await authority1.SubmitActionAsync(action);
            await authority2.SubmitActionAsync(action);
        }

        Assert.False(authority1.IsDesynced);
        Assert.False(authority2.IsDesynced);
        Assert.Equal(authority1.GetCurrentStateHash(), authority2.GetCurrentStateHash());
    }

    [Fact]
    public async Task DifferentNodes_SameReplay_SignaturesAgree()
    {
        var engine = new DefaultGameEngine();
        var authorityNode1 = new ReplayGameAuthority(Guid.NewGuid(), engine);
        var authorityNode2 = new ReplayGameAuthority(Guid.NewGuid(), engine);

        var action = AuthorityActionFactory.CreateReplayBackedAction(Guid.NewGuid(), PrimaryAction.Fire);
        await authorityNode1.SubmitActionAsync(action);
        await authorityNode2.SubmitActionAsync(action);

        var sessionAuthority = CreateSessionAuthority();
        var sessionId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        // Both nodes produced the same hash, so both signed results should verify.
        var hashBytes1 = Convert.FromHexString(authorityNode1.GetCurrentStateHash());
        var hashBytes2 = Convert.FromHexString(authorityNode2.GetCurrentStateHash());

        Assert.True(hashBytes1.AsSpan().SequenceEqual(hashBytes2));

        var signed1 = sessionAuthority.Sign(sessionId, playerId, hashBytes1);
        var signed2 = sessionAuthority.Sign(sessionId, playerId, hashBytes2);

        Assert.Equal(signed1.FinalHash, signed2.FinalHash);
        Assert.True(SessionAuthority.VerifySignedRun(signed1, sessionAuthority.ToAuthority()));
        Assert.True(SessionAuthority.VerifySignedRun(signed2, sessionAuthority.ToAuthority()));
    }

    // ──────────────────────────────────────────────────────────────────────
    // ReplayGameAuthority — desync detection
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayGameAuthority_NormalActions_NoDesync()
    {
        var authority = new ReplayGameAuthority(Guid.NewGuid(), new DefaultGameEngine());

        var actions = new[]
        {
            AuthorityActionFactory.CreateReplayBackedAction(Guid.NewGuid(), PrimaryAction.Fire),
            AuthorityActionFactory.CreateReplayBackedAction(Guid.NewGuid(), PrimaryAction.Reload)
        };

        await authority.SubmitActionAsync(actions[0]);
        await authority.SubmitActionAsync(actions[1]);

        Assert.False(authority.IsDesynced);
    }

    [Fact]
    public async Task ReplayGameAuthority_ActionLog_RecordsAllEntries()
    {
        var authority = new ReplayGameAuthority(Guid.NewGuid(), new DefaultGameEngine());
        var operatorId = Guid.NewGuid();

        var actions = AuthorityActionFactory.CreateReplayBackedActions(operatorId, 42, PrimaryAction.Fire, PrimaryAction.Fire);
        await authority.SubmitActionAsync(actions[0]);
        await authority.SubmitActionAsync(actions[1]);

        var log = authority.GetActionLog();
        Assert.Equal(2, log.Count);
        Assert.Equal(0, log[0].SequenceNumber);
        Assert.Equal(1, log[1].SequenceNumber);
    }

    [Fact]
    public async Task ReplayGameAuthority_HashIsDeterministic()
    {
        var engine = new DefaultGameEngine();
        var operatorId = Guid.NewGuid();

        var authA = new ReplayGameAuthority(Guid.NewGuid(), engine);
        var authB = new ReplayGameAuthority(Guid.NewGuid(), engine);

        var action = AuthorityActionFactory.CreateReplayBackedAction(operatorId, PrimaryAction.Fire);
        await authA.SubmitActionAsync(action);
        await authB.SubmitActionAsync(action);

        Assert.Equal(authA.GetCurrentStateHash(), authB.GetCurrentStateHash());
    }

    // ──────────────────────────────────────────────────────────────────────
    // SignedRunResult construction
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void SignedRunResult_ThrowsOnEmptyFinalHash()
    {
        var sig = new byte[64];
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), "", "auth-1", sig));
    }

    [Fact]
    public void SignedRunResult_ThrowsOnEmptyAuthorityId()
    {
        var sig = new byte[64];
        var hash = Convert.ToHexString(CreateHash(10));
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), hash, "  ", sig));
    }

    [Fact]
    public void SignedRunResult_ThrowsOnWrongSignatureLength()
    {
        var hash = Convert.ToHexString(CreateHash(10));
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), hash, "auth-1", new byte[32]));
    }

    [Fact]
    public void SignedRunResult_ThrowsOnWrongHashLength()
    {
        // 63-char string — not a valid 32-byte SHA-256 hex representation
        var shortHex = new string('A', 63);
        var sig = new byte[64];
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), shortHex, "auth-1", sig));
    }

    [Fact]
    public void SignedRunResult_Constructor_Throws_WhenFinalHashIsInvalidHex()
    {
        var sessionAuthority = CreateSessionAuthority();
        var sig = new byte[64];
        // 64 'Z' characters: valid length but non-hex characters.
        // The constructor must now reject non-hex strings at construction time.
        var nonHexHash = new string('Z', 64);
        Assert.Throws<ArgumentException>(() =>
            new SignedRunResult(Guid.NewGuid(), Guid.NewGuid(), nonHexHash, sessionAuthority.Id, sig));
    }

    [Fact]
    public void GetCurrentState_ReturnsIndependentCopy()
    {
        var authority = new ReplayGameAuthority(Guid.NewGuid(), new DefaultGameEngine());

        var state1 = authority.GetCurrentState();
        state1.Operators.Add(new GameStateDto.OperatorSnapshot { OperatorId = Guid.NewGuid(), Name = "injected" });

        // The authority's internal state must not be affected by mutation of the returned copy.
        var state2 = authority.GetCurrentState();
        Assert.Empty(state2.Operators);
    }



    private static SessionAuthority CreateSessionAuthority(string id = "test-authority")
    {
        var privateKey = SessionAuthority.GeneratePrivateKey();
        return new SessionAuthority(privateKey, id);
    }

    private static byte[] CreateHash(byte seed)
    {
        var hash = new byte[32];
        for (var i = 0; i < hash.Length; i++)
        {
            hash[i] = (byte)(seed + i);
        }

        return hash;
    }
}
