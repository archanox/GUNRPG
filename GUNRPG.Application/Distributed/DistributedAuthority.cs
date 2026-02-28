using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Deterministic lockstep distributed authority.
/// Every node runs this independently, applies actions in the same order,
/// and verifies state hashes match across all peers.
/// Game logic is delegated to the shared <see cref="IDeterministicGameEngine"/>;
/// this class is responsible only for replication ordering, hashing, and peer consensus.
/// </summary>
public sealed class DistributedAuthority : IGameAuthority
{
    private readonly ILockstepTransport _transport;
    private readonly IDeterministicGameEngine _engine;
    private readonly List<DistributedActionEntry> _actionLog = new();
    private readonly HashSet<Guid> _appliedActionIds = new();
    private readonly object _lock = new();

    // Pending outbound actions awaiting acknowledgment from all peers
    private readonly Dictionary<Guid, PendingAction> _pendingActions = new();

    // Buffered inbound actions waiting for their sequence turn
    private readonly SortedDictionary<long, ActionBroadcastMessage> _inboundBuffer = new();

    private GameStateDto _currentState;
    private long _nextSequenceNumber;
    private string _currentStateHash;
    private bool _isDesynced;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DistributedAuthority(Guid nodeId, ILockstepTransport transport, IDeterministicGameEngine engine)
    {
        NodeId = nodeId;
        _transport = transport;
        _engine = engine;
        _currentState = new GameStateDto { ActionCount = 0, Operators = new List<GameStateDto.OperatorSnapshot>() };
        _currentStateHash = ComputeHash(_currentState);

        _transport.OnActionReceived += HandleActionReceived;
        _transport.OnAckReceived += HandleAckReceived;
        _transport.OnHashReceived += HandleHashReceived;
        _transport.OnSyncRequestReceived += HandleSyncRequestReceived;
        _transport.OnSyncResponseReceived += HandleSyncResponseReceived;
        _transport.OnPeerConnected += HandlePeerConnected;
        _transport.OnPeerDisconnected += HandlePeerDisconnected;
    }

    public Guid NodeId { get; }
    public bool IsDesynced => _isDesynced;

    public async Task SubmitActionAsync(PlayerActionDto action, CancellationToken ct = default)
    {
        if (_isDesynced)
            throw new InvalidOperationException("Node is in desync state. Cannot submit actions.");

        long proposedSeq;
        lock (_lock)
        {
            proposedSeq = _nextSequenceNumber;
        }

        var broadcast = new ActionBroadcastMessage
        {
            SenderId = NodeId,
            ProposedSequenceNumber = proposedSeq,
            Action = action
        };

        var connectedPeers = _transport.ConnectedPeers;
        if (connectedPeers.Count == 0)
        {
            // Solo mode: apply immediately
            lock (_lock)
            {
                ApplyActionInternal(action, NodeId);
            }
            return;
        }

        // Register pending action
        var pending = new PendingAction(action, proposedSeq, connectedPeers);
        lock (_lock)
        {
            _pendingActions[action.ActionId] = pending;
        }

        await _transport.BroadcastActionAsync(broadcast, ct);

        // Wait for consensus (all peers acknowledged)
        await pending.WaitForConsensusAsync(ct);

        lock (_lock)
        {
            _pendingActions.Remove(action.ActionId);
            ApplyActionInternal(action, NodeId);
        }

        // Broadcast resulting hash
        string hash;
        long seq;
        lock (_lock)
        {
            hash = _currentStateHash;
            seq = _nextSequenceNumber - 1;
        }

        await _transport.BroadcastHashAsync(new HashBroadcastMessage
        {
            SenderId = NodeId,
            SequenceNumber = seq,
            StateHash = hash
        }, ct);
    }

    public GameStateDto GetCurrentState()
    {
        lock (_lock)
        {
            return _currentState;
        }
    }

    public string GetCurrentStateHash()
    {
        lock (_lock)
        {
            return _currentStateHash;
        }
    }

    public IReadOnlyList<DistributedActionEntry> GetActionLog()
    {
        lock (_lock)
        {
            return _actionLog.ToList().AsReadOnly();
        }
    }

    // --- Internal action application ---

    private void ApplyActionInternal(PlayerActionDto action, Guid originNodeId)
    {
        _currentState = _engine.Step(_currentState, action);
        var hash = ComputeHash(_currentState);
        _currentStateHash = hash;

        var seq = _nextSequenceNumber++;
        _appliedActionIds.Add(action.ActionId);
        _actionLog.Add(new DistributedActionEntry
        {
            SequenceNumber = seq,
            NodeId = originNodeId,
            Action = action,
            StateHashAfterApply = hash
        });
    }

    private static string ComputeHash(GameStateDto state)
    {
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Drains the inbound buffer, applying any actions whose sequence number
    /// matches _nextSequenceNumber in order.
    /// </summary>
    private void DrainInboundBuffer()
    {
        while (_inboundBuffer.Count > 0)
        {
            var first = _inboundBuffer.First();
            if (first.Key != _nextSequenceNumber) break;

            _inboundBuffer.Remove(first.Key);
            var msg = first.Value;

            if (!_appliedActionIds.Contains(msg.Action.ActionId))
            {
                ApplyActionInternal(msg.Action, msg.SenderId);
            }
        }
    }

    // --- Peer message handlers ---

    private void HandleActionReceived(ActionBroadcastMessage msg)
    {
        if (_isDesynced) return;

        lock (_lock)
        {
            // Buffer the action at its proposed sequence position
            if (!_inboundBuffer.ContainsKey(msg.ProposedSequenceNumber) &&
                !_appliedActionIds.Contains(msg.Action.ActionId))
            {
                _inboundBuffer[msg.ProposedSequenceNumber] = msg;
            }

            // Apply any buffered actions that are next in sequence
            DrainInboundBuffer();
        }

        // Send acknowledgment back to the sender
        _ = _transport.SendAckAsync(msg.SenderId, new ActionAckMessage
        {
            SenderId = NodeId,
            AckedActionId = msg.Action.ActionId,
            SequenceNumber = msg.ProposedSequenceNumber
        });
    }

    private void HandleAckReceived(ActionAckMessage msg)
    {
        PendingAction? pending;
        lock (_lock)
        {
            _pendingActions.TryGetValue(msg.AckedActionId, out pending);
        }

        pending?.Acknowledge(msg.SenderId);
    }

    private void HandleHashReceived(HashBroadcastMessage msg)
    {
        lock (_lock)
        {
            if (msg.SequenceNumber < _actionLog.Count)
            {
                var localEntry = _actionLog[(int)msg.SequenceNumber];
                if (localEntry.StateHashAfterApply != msg.StateHash)
                {
                    _isDesynced = true;
                }
            }
        }
    }

    private void HandleSyncRequestReceived(LogSyncRequestMessage msg)
    {
        List<DistributedActionEntry> entries;
        bool fullReplay;

        lock (_lock)
        {
            if (msg.FromSequenceNumber == 0 ||
                (msg.FromSequenceNumber <= _actionLog.Count &&
                 msg.FromSequenceNumber > 0 &&
                 _actionLog[(int)msg.FromSequenceNumber - 1].StateHashAfterApply != msg.LatestHash))
            {
                // Hash mismatch or genesis request: send full log
                entries = _actionLog.ToList();
                fullReplay = true;
            }
            else
            {
                // Send only missing entries
                entries = _actionLog.Skip((int)msg.FromSequenceNumber).ToList();
                fullReplay = false;
            }
        }

        _ = _transport.SendSyncResponseAsync(msg.SenderId, new LogSyncResponseMessage
        {
            SenderId = NodeId,
            Entries = entries,
            FullReplay = fullReplay
        });
    }

    private void HandleSyncResponseReceived(LogSyncResponseMessage msg)
    {
        lock (_lock)
        {
            if (msg.FullReplay)
            {
                // Full replay from genesis
                _actionLog.Clear();
                _appliedActionIds.Clear();
                _currentState = new GameStateDto { ActionCount = 0, Operators = new List<GameStateDto.OperatorSnapshot>() };
                _nextSequenceNumber = 0;
                _currentStateHash = ComputeHash(_currentState);
            }

            foreach (var entry in msg.Entries)
            {
                if (entry.SequenceNumber >= _nextSequenceNumber)
                {
                    ApplyActionInternal(entry.Action, entry.NodeId);

                    // Verify hash after apply
                    if (_currentStateHash != entry.StateHashAfterApply)
                    {
                        _isDesynced = true;
                        return;
                    }
                }
            }

            _isDesynced = false;
        }
    }

    private void HandlePeerConnected(Guid peerId)
    {
        // When a new peer connects, initiate sync
        string hash;
        long logLength;

        lock (_lock)
        {
            hash = _currentStateHash;
            logLength = _actionLog.Count;
        }

        _ = _transport.SendSyncRequestAsync(peerId, new LogSyncRequestMessage
        {
            SenderId = NodeId,
            FromSequenceNumber = logLength,
            LatestHash = hash
        });
    }

    private void HandlePeerDisconnected(Guid peerId)
    {
        // When a peer disconnects, mark it as acknowledged on all pending
        // outbound actions so SubmitActionAsync does not hang indefinitely.
        lock (_lock)
        {
            foreach (var pending in _pendingActions.Values)
            {
                pending.Acknowledge(peerId);
            }
        }
    }

    // --- Pending action tracking ---

    private sealed class PendingAction
    {
        private readonly HashSet<Guid> _requiredPeers;
        private readonly HashSet<Guid> _acknowledgedPeers = new();
        private readonly TaskCompletionSource _consensusTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public PlayerActionDto Action { get; }
        public long ProposedSequenceNumber { get; }

        public PendingAction(PlayerActionDto action, long proposedSeq, IReadOnlySet<Guid> connectedPeers)
        {
            Action = action;
            ProposedSequenceNumber = proposedSeq;
            _requiredPeers = new HashSet<Guid>(connectedPeers);
        }

        public bool IsConsensusReached
        {
            get
            {
                lock (_acknowledgedPeers)
                {
                    return _requiredPeers.IsSubsetOf(_acknowledgedPeers);
                }
            }
        }

        public void Acknowledge(Guid peerId)
        {
            lock (_acknowledgedPeers)
            {
                _acknowledgedPeers.Add(peerId);
                if (_requiredPeers.IsSubsetOf(_acknowledgedPeers))
                {
                    _consensusTcs.TrySetResult();
                }
            }
        }

        public async Task WaitForConsensusAsync(CancellationToken ct)
        {
            using (ct.Register(() => _consensusTcs.TrySetCanceled(ct)))
            {
                await _consensusTcs.Task.ConfigureAwait(false);
            }
        }
    }
}
