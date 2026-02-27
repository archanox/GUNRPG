using System.Text.Json;
using GUNRPG.Application.Distributed;

namespace GUNRPG.Infrastructure.Distributed;

/// <summary>
/// In-memory lockstep transport for testing and single-process multi-node scenarios.
/// Connects multiple DistributedAuthority instances in the same process.
/// </summary>
public sealed class InMemoryLockstepTransport : ILockstepTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Guid _nodeId;
    private readonly HashSet<Guid> _connectedPeers = new();
    private readonly Dictionary<Guid, InMemoryLockstepTransport> _peerTransports = new();

    public InMemoryLockstepTransport(Guid nodeId)
    {
        _nodeId = nodeId;
    }

    public IReadOnlySet<Guid> ConnectedPeers => _connectedPeers;

    public event Action<ActionBroadcastMessage>? OnActionReceived;
    public event Action<ActionAckMessage>? OnAckReceived;
    public event Action<HashBroadcastMessage>? OnHashReceived;
    public event Action<LogSyncRequestMessage>? OnSyncRequestReceived;
    public event Action<LogSyncResponseMessage>? OnSyncResponseReceived;
    public event Action<Guid>? OnPeerConnected;
    public event Action<Guid>? OnPeerDisconnected;

    /// <summary>
    /// Connect this transport to another transport instance, simulating a P2P link.
    /// </summary>
    public void ConnectTo(InMemoryLockstepTransport other)
    {
        if (_connectedPeers.Contains(other._nodeId)) return;

        _connectedPeers.Add(other._nodeId);
        _peerTransports[other._nodeId] = other;

        other._connectedPeers.Add(_nodeId);
        other._peerTransports[_nodeId] = this;

        OnPeerConnected?.Invoke(other._nodeId);
        other.OnPeerConnected?.Invoke(_nodeId);
    }

    /// <summary>
    /// Disconnect this transport from another transport instance.
    /// </summary>
    public void DisconnectFrom(InMemoryLockstepTransport other)
    {
        if (!_connectedPeers.Contains(other._nodeId)) return;

        _connectedPeers.Remove(other._nodeId);
        _peerTransports.Remove(other._nodeId);

        other._connectedPeers.Remove(_nodeId);
        other._peerTransports.Remove(_nodeId);

        OnPeerDisconnected?.Invoke(other._nodeId);
        other.OnPeerDisconnected?.Invoke(_nodeId);
    }

    public Task BroadcastActionAsync(ActionBroadcastMessage message, CancellationToken ct = default)
    {
        foreach (var peer in _peerTransports.Values.ToList())
        {
            var clone = Clone<ActionBroadcastMessage>(message);
            peer.OnActionReceived?.Invoke(clone);
        }
        return Task.CompletedTask;
    }

    public Task SendAckAsync(Guid peerId, ActionAckMessage message, CancellationToken ct = default)
    {
        if (_peerTransports.TryGetValue(peerId, out var peer))
        {
            var clone = Clone<ActionAckMessage>(message);
            peer.OnAckReceived?.Invoke(clone);
        }
        return Task.CompletedTask;
    }

    public Task BroadcastHashAsync(HashBroadcastMessage message, CancellationToken ct = default)
    {
        foreach (var peer in _peerTransports.Values.ToList())
        {
            var clone = Clone<HashBroadcastMessage>(message);
            peer.OnHashReceived?.Invoke(clone);
        }
        return Task.CompletedTask;
    }

    public Task SendSyncRequestAsync(Guid peerId, LogSyncRequestMessage message, CancellationToken ct = default)
    {
        if (_peerTransports.TryGetValue(peerId, out var peer))
        {
            var clone = Clone<LogSyncRequestMessage>(message);
            peer.OnSyncRequestReceived?.Invoke(clone);
        }
        return Task.CompletedTask;
    }

    public Task SendSyncResponseAsync(Guid peerId, LogSyncResponseMessage message, CancellationToken ct = default)
    {
        if (_peerTransports.TryGetValue(peerId, out var peer))
        {
            var clone = Clone<LogSyncResponseMessage>(message);
            peer.OnSyncResponseReceived?.Invoke(clone);
        }
        return Task.CompletedTask;
    }

    /// <summary>Deep-clone a message via JSON round-trip to simulate network serialization.</summary>
    private static T Clone<T>(T obj) where T : class
    {
        var json = JsonSerializer.Serialize(obj, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)!;
    }

    /// <summary>
    /// Simulate receiving a hash broadcast message (for testing desync scenarios).
    /// </summary>
    public void SimulateIncomingHash(HashBroadcastMessage message)
    {
        OnHashReceived?.Invoke(message);
    }
}
