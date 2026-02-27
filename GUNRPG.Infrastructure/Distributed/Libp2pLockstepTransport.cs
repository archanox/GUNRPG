using System.Text.Json;
using GUNRPG.Application.Distributed;
using Nethermind.Libp2p.Core;

namespace GUNRPG.Infrastructure.Distributed;

/// <summary>
/// Libp2p-based lockstep transport using the /gunrpg/lockstep/1.0.0 protocol.
/// Wraps Nethermind.Libp2p for peer-to-peer communication.
/// </summary>
public sealed class Libp2pLockstepTransport : ILockstepTransport, ISessionProtocol, ISessionListenerProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Guid _nodeId;
    private readonly HashSet<Guid> _connectedPeers = new();
    private readonly Dictionary<Guid, IChannel> _peerChannels = new();
    private readonly object _lock = new();

    public Libp2pLockstepTransport(Guid nodeId)
    {
        _nodeId = nodeId;
    }

    // IProtocol implementation
    public string Id => LockstepProtocol.Id;

    public IReadOnlySet<Guid> ConnectedPeers
    {
        get { lock (_lock) return new HashSet<Guid>(_connectedPeers); }
    }

    public event Action<ActionBroadcastMessage>? OnActionReceived;
    public event Action<ActionAckMessage>? OnAckReceived;
    public event Action<HashBroadcastMessage>? OnHashReceived;
    public event Action<LogSyncRequestMessage>? OnSyncRequestReceived;
    public event Action<LogSyncResponseMessage>? OnSyncResponseReceived;
    public event Action<Guid>? OnPeerConnected;
    public event Action<Guid>? OnPeerDisconnected;

    // ISessionProtocol - called when dialing a remote peer
    public async Task DialAsync(IChannel channel, ISessionContext context)
    {
        await HandleChannelAsync(channel, context);
    }

    // ISessionListenerProtocol - called when a remote peer connects
    public async Task ListenAsync(IChannel channel, ISessionContext context)
    {
        await HandleChannelAsync(channel, context);
    }

    private async Task HandleChannelAsync(IChannel channel, ISessionContext context)
    {
        // Exchange node IDs as hello using line-based protocol
        await channel.WriteLineAsync(_nodeId.ToString());
        var remotePeerIdStr = await channel.ReadLineAsync();
        if (!Guid.TryParse(remotePeerIdStr, out var remotePeerId)) return;

        lock (_lock)
        {
            _connectedPeers.Add(remotePeerId);
            _peerChannels[remotePeerId] = channel;
        }

        OnPeerConnected?.Invoke(remotePeerId);

        try
        {
            // Read loop for incoming messages (line-delimited JSON)
            while (!channel.CancellationToken.IsCancellationRequested)
            {
                var json = await channel.ReadLineAsync();
                if (json == null) break;
                DispatchMessage(json);
            }
        }
        finally
        {
            lock (_lock)
            {
                _connectedPeers.Remove(remotePeerId);
                _peerChannels.Remove(remotePeerId);
            }
            OnPeerDisconnected?.Invoke(remotePeerId);
        }
    }

    private void DispatchMessage(string json)
    {
        // Messages are prefixed with type indicator
        if (json.StartsWith("{\"type\":\"action_broadcast\""))
        {
            var wrapper = JsonSerializer.Deserialize<MessageWrapper<ActionBroadcastMessage>>(json, JsonOptions);
            if (wrapper?.Payload != null) OnActionReceived?.Invoke(wrapper.Payload);
        }
        else if (json.StartsWith("{\"type\":\"action_ack\""))
        {
            var wrapper = JsonSerializer.Deserialize<MessageWrapper<ActionAckMessage>>(json, JsonOptions);
            if (wrapper?.Payload != null) OnAckReceived?.Invoke(wrapper.Payload);
        }
        else if (json.StartsWith("{\"type\":\"hash_broadcast\""))
        {
            var wrapper = JsonSerializer.Deserialize<MessageWrapper<HashBroadcastMessage>>(json, JsonOptions);
            if (wrapper?.Payload != null) OnHashReceived?.Invoke(wrapper.Payload);
        }
        else if (json.StartsWith("{\"type\":\"sync_request\""))
        {
            var wrapper = JsonSerializer.Deserialize<MessageWrapper<LogSyncRequestMessage>>(json, JsonOptions);
            if (wrapper?.Payload != null) OnSyncRequestReceived?.Invoke(wrapper.Payload);
        }
        else if (json.StartsWith("{\"type\":\"sync_response\""))
        {
            var wrapper = JsonSerializer.Deserialize<MessageWrapper<LogSyncResponseMessage>>(json, JsonOptions);
            if (wrapper?.Payload != null) OnSyncResponseReceived?.Invoke(wrapper.Payload);
        }
    }

    public Task BroadcastActionAsync(ActionBroadcastMessage message, CancellationToken ct = default)
        => BroadcastAsync("action_broadcast", message, ct);

    public Task SendAckAsync(Guid peerId, ActionAckMessage message, CancellationToken ct = default)
        => SendToAsync(peerId, "action_ack", message, ct);

    public Task BroadcastHashAsync(HashBroadcastMessage message, CancellationToken ct = default)
        => BroadcastAsync("hash_broadcast", message, ct);

    public Task SendSyncRequestAsync(Guid peerId, LogSyncRequestMessage message, CancellationToken ct = default)
        => SendToAsync(peerId, "sync_request", message, ct);

    public Task SendSyncResponseAsync(Guid peerId, LogSyncResponseMessage message, CancellationToken ct = default)
        => SendToAsync(peerId, "sync_response", message, ct);

    private async Task BroadcastAsync<T>(string type, T message, CancellationToken ct) where T : class
    {
        List<IChannel> channels;
        lock (_lock)
        {
            channels = _peerChannels.Values.ToList();
        }

        var json = JsonSerializer.Serialize(new MessageWrapper<T> { Type = type, Payload = message }, JsonOptions);

        foreach (var channel in channels)
        {
            await channel.WriteLineAsync(json);
        }
    }

    private async Task SendToAsync<T>(Guid peerId, string type, T message, CancellationToken ct) where T : class
    {
        IChannel? channel;
        lock (_lock)
        {
            _peerChannels.TryGetValue(peerId, out channel);
        }

        if (channel == null) return;

        var json = JsonSerializer.Serialize(new MessageWrapper<T> { Type = type, Payload = message }, JsonOptions);
        await channel.WriteLineAsync(json);
    }

    private sealed class MessageWrapper<T>
    {
        public string Type { get; set; } = string.Empty;
        public T? Payload { get; set; }
    }
}
