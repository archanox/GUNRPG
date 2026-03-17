using GUNRPG.Ledger;
using GUNRPG.Security;

namespace GUNRPG.Tests;

public sealed class AuthorityLedgerTests
{
    private static readonly DateTimeOffset ReferenceNow = new(2026, 03, 17, 06, 00, 00, TimeSpan.Zero);

    [Fact]
    public void AuthorityState_RebuildsDeterministically()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out var privateKeyC);
        var authorityD = CreateAuthority("authority-d", out _);
        var authorityE = CreateAuthority("authority-e", out _);
        var ledgerA = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var ledgerB = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var policy = new QuorumPolicy(2);
        var validator = new QuorumValidator();
        var addEvent = new AuthorityAdded(authorityD.PublicKey);
        var rotateEvent = new AuthorityRotated(authorityB.PublicKey, authorityE.PublicKey);

        Assert.True(ledgerA.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [
                SignAuthorityEvent(addEvent, authorityA, privateKeyA),
                SignAuthorityEvent(addEvent, authorityB, privateKeyB)
            ],
            validator,
            policy,
            ReferenceNow));
        Assert.True(ledgerA.TryAppendAuthorityEventWithQuorum(
            rotateEvent,
            [
                SignAuthorityEvent(rotateEvent, authorityA, privateKeyA),
                SignAuthorityEvent(rotateEvent, authorityC, privateKeyC)
            ],
            validator,
            policy,
            ReferenceNow.AddMinutes(1)));

        Assert.True(ledgerB.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [
                SignAuthorityEvent(addEvent, authorityA, privateKeyA),
                SignAuthorityEvent(addEvent, authorityB, privateKeyB)
            ],
            validator,
            policy,
            ReferenceNow));
        Assert.True(ledgerB.TryAppendAuthorityEventWithQuorum(
            rotateEvent,
            [
                SignAuthorityEvent(rotateEvent, authorityA, privateKeyA),
                SignAuthorityEvent(rotateEvent, authorityC, privateKeyC)
            ],
            validator,
            policy,
            ReferenceNow.AddMinutes(1)));

        var stateA = AuthorityState.BuildFromLedger(ledgerA);
        var stateB = AuthorityState.BuildFromLedger(ledgerB);

        Assert.Equal(stateA.Count, stateB.Count);
        Assert.Equal(stateA.IsTrusted(authorityA.PublicKey), stateB.IsTrusted(authorityA.PublicKey));
        Assert.Equal(stateA.IsTrusted(authorityB.PublicKey), stateB.IsTrusted(authorityB.PublicKey));
        Assert.Equal(stateA.IsTrusted(authorityC.PublicKey), stateB.IsTrusted(authorityC.PublicKey));
        Assert.Equal(stateA.IsTrusted(authorityD.PublicKey), stateB.IsTrusted(authorityD.PublicKey));
        Assert.Equal(stateA.IsTrusted(authorityE.PublicKey), stateB.IsTrusted(authorityE.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_AddAuthority()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var addEvent = new AuthorityAdded(authorityD.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [
                SignAuthorityEvent(addEvent, authorityA, privateKeyA),
                SignAuthorityEvent(addEvent, authorityB, privateKeyB)
            ],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        var authorityState = AuthorityState.BuildFromLedger(ledger);
        Assert.True(appended);
        Assert.True(authorityState.IsTrusted(authorityD.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_RemoveAuthority()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var removeEvent = new AuthorityRemoved(authorityC.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            removeEvent,
            [
                SignAuthorityEvent(removeEvent, authorityA, privateKeyA),
                SignAuthorityEvent(removeEvent, authorityB, privateKeyB)
            ],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        var authorityState = AuthorityState.BuildFromLedger(ledger);
        Assert.True(appended);
        Assert.False(authorityState.IsTrusted(authorityC.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_RotateAuthority()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var rotateEvent = new AuthorityRotated(authorityB.PublicKey, authorityD.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            rotateEvent,
            [
                SignAuthorityEvent(rotateEvent, authorityA, privateKeyA),
                SignAuthorityEvent(rotateEvent, authorityB, privateKeyB)
            ],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        var authorityState = AuthorityState.BuildFromLedger(ledger);
        Assert.True(appended);
        Assert.False(authorityState.IsTrusted(authorityB.PublicKey));
        Assert.True(authorityState.IsTrusted(authorityD.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_RequiresQuorum()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out _);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var addEvent = new AuthorityAdded(authorityD.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [
                SignAuthorityEvent(addEvent, authorityA, privateKeyA)
            ],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        Assert.False(appended);
        Assert.False(AuthorityState.BuildFromLedger(ledger).IsTrusted(authorityD.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_PreventsSelfApproval()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out _);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out var privateKeyD);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var addEvent = new AuthorityAdded(authorityD.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [
                SignAuthorityEvent(addEvent, authorityA, privateKeyA),
                SignAuthorityEvent(addEvent, authorityD, privateKeyD)
            ],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        Assert.False(appended);
        Assert.False(AuthorityState.BuildFromLedger(ledger).IsTrusted(authorityD.PublicKey));
    }

    [Fact]
    public void AuthorityEvent_PreventsRemovingTooManyAuthorities()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(3));
        var validator = new QuorumValidator();
        var removeEvent = new AuthorityRemoved(authorityC.PublicKey);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            removeEvent,
            [
                SignAuthorityEvent(removeEvent, authorityA, privateKeyA),
                SignAuthorityEvent(removeEvent, authorityB, privateKeyB)
            ],
            validator,
            new QuorumPolicy(3),
            ReferenceNow);

        Assert.False(appended);
        Assert.True(AuthorityState.BuildFromLedger(ledger).IsTrusted(authorityC.PublicKey));
    }

    [Fact]
    public void AuthorityState_CacheMatchesRebuild()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out var privateKeyC);
        var authorityD = CreateAuthority("authority-d", out _);
        var authorityE = CreateAuthority("authority-e", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();

        var addEvent = new AuthorityAdded(authorityD.PublicKey);
        var rotateEvent = new AuthorityRotated(authorityB.PublicKey, authorityE.PublicKey);
        Assert.True(ledger.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [SignAuthorityEvent(addEvent, authorityA, privateKeyA), SignAuthorityEvent(addEvent, authorityB, privateKeyB)],
            validator,
            new QuorumPolicy(2),
            ReferenceNow));
        Assert.True(ledger.TryAppendAuthorityEventWithQuorum(
            rotateEvent,
            [SignAuthorityEvent(rotateEvent, authorityA, privateKeyA), SignAuthorityEvent(rotateEvent, authorityC, privateKeyC)],
            validator,
            new QuorumPolicy(2),
            ReferenceNow.AddMinutes(1)));

        var rebuilt = AuthorityState.BuildFromLedger(ledger);
        var cached = ledger.CurrentAuthorityState;

        Assert.True(cached.IsEquivalentTo(rebuilt));
    }

    [Fact]
    public void AuthorityState_IncrementalUpdateMatchesFullRebuild()
    {
        var authorityA = CreateAuthority("authority-a", out _);
        var authorityB = CreateAuthority("authority-b", out _);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var authorityE = CreateAuthority("authority-e", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));

        ledger.Append(new AuthorityAdded(authorityD.PublicKey), [CreateDummySignature(authorityA), CreateDummySignature(authorityB)], ReferenceNow);
        ledger.Append(new AuthorityRotated(authorityB.PublicKey, authorityE.PublicKey), [CreateDummySignature(authorityA), CreateDummySignature(authorityC)], ReferenceNow.AddMinutes(1));

        Assert.True(ledger.CurrentAuthorityState.IsEquivalentTo(AuthorityState.BuildFromLedger(ledger)));
    }

    [Fact]
    public void Ledger_Verify_DetectsInvalidAuthorityTransition()
    {
        var authorityA = CreateAuthority("authority-a", out _);
        var authorityB = CreateAuthority("authority-b", out _);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));

        var invalidEvent = new AuthorityAdded(authorityD.PublicKey);
        ledger.Append(invalidEvent, [CreateDummySignature(authorityD)], ReferenceNow);

        Assert.False(ledger.Verify());
    }

    [Fact]
    public void SignatureNormalization_RemovesDuplicates()
    {
        var authorityA = CreateAuthority("authority-a", out var privateKeyA);
        var authorityB = CreateAuthority("authority-b", out var privateKeyB);
        var authorityC = CreateAuthority("authority-c", out _);
        var authorityD = CreateAuthority("authority-d", out _);
        var ledger = new RunLedger([authorityA, authorityB, authorityC], new QuorumPolicy(2));
        var validator = new QuorumValidator();
        var addEvent = new AuthorityAdded(authorityD.PublicKey);
        var duplicateSignature = SignAuthorityEvent(addEvent, authorityA, privateKeyA);

        var appended = ledger.TryAppendAuthorityEventWithQuorum(
            addEvent,
            [duplicateSignature, duplicateSignature, SignAuthorityEvent(addEvent, authorityB, privateKeyB)],
            validator,
            new QuorumPolicy(2),
            ReferenceNow);

        Assert.True(appended);
        Assert.Single(ledger.Entries);
        Assert.Equal(2, ledger.Entries[0].AuthoritySignatures.Length);
    }

    private static Authority CreateAuthority(string id, out byte[] privateKey)
    {
        privateKey = AuthorityCrypto.GeneratePrivateKey();
        return new Authority(AuthorityCrypto.GetPublicKey(privateKey), id);
    }

    private static AuthoritySignature SignAuthorityEvent(
        AuthorityEvent authorityEvent,
        Authority authority,
        byte[] privateKey)
    {
        return new AuthoritySignature(
            authority.PublicKey,
            AuthorityCrypto.SignHashedPayload(privateKey, AuthorityCrypto.ComputeAuthorityEventHash(authorityEvent)));
    }

    private static AuthoritySignature CreateDummySignature(Authority authority)
    {
        return new AuthoritySignature(
            authority.PublicKey,
            new byte[AuthorityCrypto.SignatureSize]);
    }
}
