using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GUNRPG.Application.Distributed;

/// <summary>
/// Deterministic lockstep distributed authority.
/// Every node runs this independently, applies actions in the same order,
/// and verifies state hashes match across all peers.
/// </summary>
public sealed class DistributedAuthority : IGameAuthority
{
    private readonly ILockstepTransport _transport;
    private readonly List<DistributedActionEntry> _actionLog = new();
    private readonly Dictionary<Guid, GameStateDto.OperatorSnapshot> _operatorStates = new();
    private readonly object _lock = new();

    // Pending outbound actions awaiting acknowledgment from all peers
    private readonly Dictionary<Guid, PendingAction> _pendingActions = new();

    // Buffered inbound actions waiting for their sequence turn
    private readonly SortedDictionary<long, (ActionBroadcastMessage Message, bool Acked)> _inboundBuffer = new();

    private long _nextSequenceNumber;
    private string _currentStateHash = ComputeEmptyHash();
    private bool _isDesynced;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DistributedAuthority(Guid nodeId, ILockstepTransport transport)
    {
        NodeId = nodeId;
        _transport = transport;

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
            return new GameStateDto
            {
                ActionCount = _actionLog.Count,
                Operators = _operatorStates
                    .OrderBy(kv => kv.Key)
                    .Select(kv => kv.Value)
                    .ToList()
            };
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
        // Ensure operator exists in state
        if (!_operatorStates.ContainsKey(action.OperatorId))
        {
            _operatorStates[action.OperatorId] = new GameStateDto.OperatorSnapshot
            {
                OperatorId = action.OperatorId,
                Name = $"Operator-{action.OperatorId.ToString()[..8]}",
                CurrentHealth = 100f,
                MaxHealth = 100f,
                EquippedWeaponName = "Default",
                UnlockedPerks = new List<string>()
            };
        }

        // Apply action deterministically
        var snapshot = _operatorStates[action.OperatorId];
        _operatorStates[action.OperatorId] = ApplyActionToSnapshot(snapshot, action);

        var seq = _nextSequenceNumber++;
        var hash = ComputeStateHash();
        _currentStateHash = hash;

        _actionLog.Add(new DistributedActionEntry
        {
            SequenceNumber = seq,
            NodeId = originNodeId,
            Action = action,
            StateHashAfterApply = hash
        });
    }

    private static GameStateDto.OperatorSnapshot ApplyActionToSnapshot(
        GameStateDto.OperatorSnapshot snapshot, PlayerActionDto action)
    {
        // Deterministic action application
        var health = snapshot.CurrentHealth;
        var xp = snapshot.TotalXp;

        if (action.Primary == Core.Intents.PrimaryAction.Fire)
        {
            // Firing costs no health but earns XP
            xp += 10;
        }

        if (action.Primary == Core.Intents.PrimaryAction.Reload)
        {
            // Reload is a non-damaging action
            xp += 1;
        }

        return new GameStateDto.OperatorSnapshot
        {
            OperatorId = snapshot.OperatorId,
            Name = snapshot.Name,
            TotalXp = xp,
            CurrentHealth = health,
            MaxHealth = snapshot.MaxHealth,
            EquippedWeaponName = snapshot.EquippedWeaponName,
            UnlockedPerks = snapshot.UnlockedPerks.ToList(),
            ExfilStreak = snapshot.ExfilStreak,
            IsDead = snapshot.IsDead
        };
    }

    private string ComputeStateHash()
    {
        var state = new GameStateDto
        {
            ActionCount = _actionLog.Count + 1, // Include the action being added
            Operators = _operatorStates
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .ToList()
        };

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private static string ComputeEmptyHash()
    {
        var emptyState = new GameStateDto
        {
            ActionCount = 0,
            Operators = new List<GameStateDto.OperatorSnapshot>()
        };

        var json = JsonSerializer.Serialize(emptyState, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
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
            var msg = first.Value.Message;

            // Only apply if not already in the log (dedup)
            if (!_actionLog.Any(e => e.Action.ActionId == msg.Action.ActionId))
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
                !_actionLog.Any(e => e.Action.ActionId == msg.Action.ActionId))
            {
                _inboundBuffer[msg.ProposedSequenceNumber] = (msg, false);
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
                _operatorStates.Clear();
                _nextSequenceNumber = 0;
                _currentStateHash = ComputeEmptyHash();
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
