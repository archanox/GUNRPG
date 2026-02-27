using GUNRPG.Application.Distributed;
using GUNRPG.Core.Intents;
using GUNRPG.Infrastructure.Distributed;

namespace GUNRPG.Tests;

public class DistributedAuthorityTests
{
    private static readonly Guid OperatorA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // --- Core Functionality ---

    [Fact]
    public async Task SubmitAction_Solo_AppliesImmediately()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        var action = new PlayerActionDto
        {
            OperatorId = OperatorA,
            Primary = PrimaryAction.Fire
        };

        await authority.SubmitActionAsync(action);

        var log = authority.GetActionLog();
        Assert.Single(log);
        Assert.Equal(0, log[0].SequenceNumber);
        Assert.Equal(nodeId, log[0].NodeId);
        Assert.Equal(action.ActionId, log[0].Action.ActionId);
        Assert.False(string.IsNullOrEmpty(log[0].StateHashAfterApply));
    }

    [Fact]
    public async Task SubmitAction_Solo_UpdatesGameState()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        await authority.SubmitActionAsync(new PlayerActionDto
        {
            OperatorId = OperatorA,
            Primary = PrimaryAction.Fire
        });

        var state = authority.GetCurrentState();
        Assert.Equal(1, state.ActionCount);
        Assert.Single(state.Operators);
        Assert.Equal(OperatorA, state.Operators[0].OperatorId);
        Assert.Equal(10, state.Operators[0].TotalXp); // Fire = 10 XP
    }

    [Fact]
    public async Task SubmitAction_Solo_SequenceIncreases()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });
        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload });

        var log = authority.GetActionLog();
        Assert.Equal(2, log.Count);
        Assert.Equal(0, log[0].SequenceNumber);
        Assert.Equal(1, log[1].SequenceNumber);
    }

    // --- Hashing ---

    [Fact]
    public async Task GetCurrentStateHash_ReturnsSHA256HexString()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        var hash = authority.GetCurrentStateHash();
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length); // SHA256 = 64 hex chars
        Assert.Matches("^[A-F0-9]+$", hash); // Uppercase hex
    }

    [Fact]
    public async Task StateHash_IdenticalActions_ProduceSameHash()
    {
        // Two independent nodes applying same actions should produce same hash
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeA);
        var transportB = new InMemoryLockstepTransport(nodeB);
        var authorityA = new DistributedAuthority(nodeA, transportA);
        var authorityB = new DistributedAuthority(nodeB, transportB);

        var action1 = new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire };
        var action2 = new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload };

        await authorityA.SubmitActionAsync(action1);
        await authorityA.SubmitActionAsync(action2);

        await authorityB.SubmitActionAsync(action1);
        await authorityB.SubmitActionAsync(action2);

        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
    }

    [Fact]
    public async Task StateHash_DifferentActions_ProduceDifferentHash()
    {
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeA);
        var transportB = new InMemoryLockstepTransport(nodeB);
        var authorityA = new DistributedAuthority(nodeA, transportA);
        var authorityB = new DistributedAuthority(nodeB, transportB);

        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });
        await authorityB.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload });

        Assert.NotEqual(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
    }

    [Fact]
    public async Task ActionLogEntry_HasStateHashAfterApply()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        var entry = authority.GetActionLog()[0];
        Assert.NotNull(entry.StateHashAfterApply);
        Assert.Equal(64, entry.StateHashAfterApply.Length);
        Assert.Equal(authority.GetCurrentStateHash(), entry.StateHashAfterApply);
    }

    // --- Two-Node Lockstep ---

    [Fact]
    public async Task TwoNodes_ActionReplicates()
    {
        var (authorityA, authorityB, _, _) = CreateConnectedPair();

        var action = new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire };
        await authorityA.SubmitActionAsync(action);

        // Both nodes should have the same state hash after action is applied
        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
    }

    [Fact]
    public async Task TwoNodes_ActionLogMatches()
    {
        var (authorityA, authorityB, _, _) = CreateConnectedPair();

        var action = new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire };
        await authorityA.SubmitActionAsync(action);

        var logA = authorityA.GetActionLog();
        var logB = authorityB.GetActionLog();

        Assert.Single(logA);
        Assert.Single(logB);
        Assert.Equal(logA[0].Action.ActionId, logB[0].Action.ActionId);
        Assert.Equal(logA[0].StateHashAfterApply, logB[0].StateHashAfterApply);
    }

    [Fact]
    public async Task TwoNodes_MultipleActions_MatchingState()
    {
        var (authorityA, authorityB, _, _) = CreateConnectedPair();

        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload });
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
        Assert.Equal(3, authorityA.GetActionLog().Count);
        Assert.Equal(3, authorityB.GetActionLog().Count);
    }

    // --- Desync Detection ---

    [Fact]
    public void InitialState_NotDesynced()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        Assert.False(authority.IsDesynced);
    }

    [Fact]
    public async Task Desynced_RejectsActions()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        // Submit an action first
        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        // Simulate desync by sending a hash mismatch
        transport.SimulateIncomingHash(new HashBroadcastMessage
        {
            SenderId = Guid.NewGuid(),
            SequenceNumber = 0,
            StateHash = "BADBEEF000000000000000000000000000000000000000000000000000000000"
        });

        Assert.True(authority.IsDesynced);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire }));
    }

    // --- Reconnect Sync ---

    [Fact]
    public async Task Reconnect_NodeBSyncsFromNodeA()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var authorityA = new DistributedAuthority(nodeIdA, transportA);
        var authorityB = new DistributedAuthority(nodeIdB, transportB);

        // Node A submits actions while solo
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload });
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        Assert.Equal(3, authorityA.GetActionLog().Count);
        Assert.Empty(authorityB.GetActionLog());

        // Node B connects to Node A - sync should occur
        transportA.ConnectTo(transportB);

        // After sync, Node B should have the same log and hash
        Assert.Equal(3, authorityB.GetActionLog().Count);
        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
        Assert.False(authorityB.IsDesynced);
    }

    [Fact]
    public async Task Reconnect_DisconnectAndReconnect_SyncsCorrectly()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var authorityA = new DistributedAuthority(nodeIdA, transportA);
        var authorityB = new DistributedAuthority(nodeIdB, transportB);

        // Connect and submit initial action
        transportA.ConnectTo(transportB);
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });
        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());

        // Disconnect
        transportA.DisconnectFrom(transportB);

        // Node A submits more actions while disconnected
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Reload });
        await authorityA.SubmitActionAsync(new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire });

        Assert.Equal(3, authorityA.GetActionLog().Count);
        Assert.Equal(1, authorityB.GetActionLog().Count);

        // Reconnect
        transportA.ConnectTo(transportB);

        // Node B should sync missing actions
        Assert.Equal(3, authorityB.GetActionLog().Count);
        Assert.Equal(authorityA.GetCurrentStateHash(), authorityB.GetCurrentStateHash());
    }

    // --- Action Entry Record ---

    [Fact]
    public void DistributedActionEntry_IsRecord()
    {
        var entry = new DistributedActionEntry
        {
            SequenceNumber = 42,
            NodeId = Guid.NewGuid(),
            Action = new PlayerActionDto { OperatorId = OperatorA, Primary = PrimaryAction.Fire },
            StateHashAfterApply = "ABC123"
        };

        Assert.Equal(42, entry.SequenceNumber);
        Assert.Equal(PrimaryAction.Fire, entry.Action.Primary);
        Assert.Equal("ABC123", entry.StateHashAfterApply);
    }

    // --- Message Types ---

    [Fact]
    public void ActionBroadcastMessage_HasRequiredFields()
    {
        var msg = new ActionBroadcastMessage
        {
            SenderId = Guid.NewGuid(),
            ProposedSequenceNumber = 0,
            Action = new PlayerActionDto { OperatorId = OperatorA }
        };

        Assert.NotEqual(Guid.Empty, msg.SenderId);
        Assert.Equal(0, msg.ProposedSequenceNumber);
        Assert.NotNull(msg.Action);
    }

    [Fact]
    public void ActionAckMessage_HasRequiredFields()
    {
        var msg = new ActionAckMessage
        {
            SenderId = Guid.NewGuid(),
            AckedActionId = Guid.NewGuid(),
            SequenceNumber = 5
        };

        Assert.NotEqual(Guid.Empty, msg.SenderId);
        Assert.NotEqual(Guid.Empty, msg.AckedActionId);
        Assert.Equal(5, msg.SequenceNumber);
    }

    [Fact]
    public void HashBroadcastMessage_HasRequiredFields()
    {
        var msg = new HashBroadcastMessage
        {
            SenderId = Guid.NewGuid(),
            SequenceNumber = 3,
            StateHash = "ABCDEF123456"
        };

        Assert.Equal(3, msg.SequenceNumber);
        Assert.Equal("ABCDEF123456", msg.StateHash);
    }

    [Fact]
    public void LogSyncRequestMessage_HasRequiredFields()
    {
        var msg = new LogSyncRequestMessage
        {
            SenderId = Guid.NewGuid(),
            FromSequenceNumber = 10,
            LatestHash = "HASH123"
        };

        Assert.Equal(10, msg.FromSequenceNumber);
    }

    [Fact]
    public void LogSyncResponseMessage_HasRequiredFields()
    {
        var msg = new LogSyncResponseMessage
        {
            SenderId = Guid.NewGuid(),
            Entries = new List<DistributedActionEntry>(),
            FullReplay = true
        };

        Assert.Empty(msg.Entries);
        Assert.True(msg.FullReplay);
    }

    // --- Protocol ---

    [Fact]
    public void LockstepProtocol_HasCorrectId()
    {
        Assert.Equal("/gunrpg/lockstep/1.0.0", LockstepProtocol.Id);
    }

    // --- GameStateDto ---

    [Fact]
    public async Task GameStateDto_OrdersOperatorsDeterministically()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        var authority = new DistributedAuthority(nodeId, transport);

        var opB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var opA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        // Submit action for opB first, then opA
        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = opB, Primary = PrimaryAction.Fire });
        await authority.SubmitActionAsync(new PlayerActionDto { OperatorId = opA, Primary = PrimaryAction.Reload });

        var state = authority.GetCurrentState();
        Assert.Equal(2, state.Operators.Count);
        // Should be ordered by GUID (opA < opB)
        Assert.Equal(opA, state.Operators[0].OperatorId);
        Assert.Equal(opB, state.Operators[1].OperatorId);
    }

    // --- IGameAuthority interface ---

    [Fact]
    public void DistributedAuthority_ImplementsIGameAuthority()
    {
        var nodeId = Guid.NewGuid();
        var transport = new InMemoryLockstepTransport(nodeId);
        IGameAuthority authority = new DistributedAuthority(nodeId, transport);

        Assert.NotEqual(Guid.Empty, authority.NodeId);
        Assert.False(authority.IsDesynced);
    }

    // --- Helpers ---

    private static (DistributedAuthority authorityA, DistributedAuthority authorityB,
        InMemoryLockstepTransport transportA, InMemoryLockstepTransport transportB) CreateConnectedPair()
    {
        var nodeIdA = Guid.NewGuid();
        var nodeIdB = Guid.NewGuid();
        var transportA = new InMemoryLockstepTransport(nodeIdA);
        var transportB = new InMemoryLockstepTransport(nodeIdB);
        var authorityA = new DistributedAuthority(nodeIdA, transportA);
        var authorityB = new DistributedAuthority(nodeIdB, transportB);
        transportA.ConnectTo(transportB);
        return (authorityA, authorityB, transportA, transportB);
    }
}
