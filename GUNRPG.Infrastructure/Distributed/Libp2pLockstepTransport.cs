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
    public event Action<OperatorEventBroadcastMessage>? OnOperatorEventReceived;
    public event Action<OperatorEventSyncRequestMessage>? OnOperatorEventSyncRequestReceived;
    public event Action<OperatorEventSyncResponseMessage>? OnOperatorEventSyncResponseReceived;

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

        bool isNewPeer;
        lock (_lock)
        {
            if (!_connectedPeers.Contains(remotePeerId))
            {
                isNewPeer = true;
                _connectedPeers.Add(remotePeerId);
                _peerChannels[remotePeerId] = channel;
            }
            else
            {
                isNewPeer = false;
                // Deterministic tie-break for simultaneous dials: the peer with the higher
                // node ID keeps the session it currently holds. The lower-ID peer replaces
                // its stored channel with the incoming one so both sides converge on the
                // same underlying TCP connection.
                if (_nodeId.CompareTo(remotePeerId) < 0)
                {
                    // We are the lower-ID peer: accept this channel as the canonical one.
                    _peerChannels[remotePeerId] = channel;
                }
                else
                {
                    // We are the higher-ID peer: keep the existing channel, reject this one.
                    return;
                }
            }
        }

        if (isNewPeer) OnPeerConnected?.Invoke(remotePeerId);

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
            // Only clean up peer tracking if this specific channel is still the active one.
            // In a simultaneous-dial race the lower-ID peer may have replaced its channel,
            // so the superseded channel's finally block must not evict the new registration
            // or fire a spurious disconnect event.
            bool wasActive;
            lock (_lock)
            {
                wasActive = _peerChannels.TryGetValue(remotePeerId, out var activeChannel)
                            && ReferenceEquals(activeChannel, channel);
                if (wasActive)
                {
                    _connectedPeers.Remove(remotePeerId);
                    _peerChannels.Remove(remotePeerId);
                }
            }
            if (wasActive) OnPeerDisconnected?.Invoke(remotePeerId);
        }
    }

    private void DispatchMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeProperty))
                return;

            var type = typeProperty.GetString();
            if (string.IsNullOrEmpty(type))
                return;

            switch (type)
            {
                case "action_broadcast":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<ActionBroadcastMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnActionReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "action_ack":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<ActionAckMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnAckReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "hash_broadcast":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<HashBroadcastMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnHashReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "sync_request":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<LogSyncRequestMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnSyncRequestReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "sync_response":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<LogSyncResponseMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnSyncResponseReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "operator_event":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<OperatorEventBroadcastMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnOperatorEventReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "operator_event_sync_request":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<OperatorEventSyncRequestMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnOperatorEventSyncRequestReceived?.Invoke(wrapper.Payload);
                    break;
                }
                case "operator_event_sync_response":
                {
                    var wrapper = JsonSerializer.Deserialize<MessageWrapper<OperatorEventSyncResponseMessage>>(json, JsonOptions);
                    if (wrapper?.Payload != null) OnOperatorEventSyncResponseReceived?.Invoke(wrapper.Payload);
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Malformed JSON; ignore to avoid tearing down the session loop
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

    public Task BroadcastOperatorEventAsync(OperatorEventBroadcastMessage message, CancellationToken ct = default)
        => BroadcastAsync("operator_event", message, ct);

    public Task SendOperatorEventSyncRequestAsync(Guid peerId, OperatorEventSyncRequestMessage message, CancellationToken ct = default)
        => SendToAsync(peerId, "operator_event_sync_request", message, ct);

    public Task SendOperatorEventSyncResponseAsync(Guid peerId, OperatorEventSyncResponseMessage message, CancellationToken ct = default)
        => SendToAsync(peerId, "operator_event_sync_response", message, ct);

    private async Task BroadcastAsync<T>(string type, T message, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();

        List<IChannel> channels;
        lock (_lock)
        {
            channels = _peerChannels.Values.ToList();
        }

        var json = JsonSerializer.Serialize(new MessageWrapper<T> { Type = type, Payload = message }, JsonOptions);

        var tasks = channels.Select(channel => channel.WriteLineAsync(json).AsTask()).ToArray();
        await Task.WhenAll(tasks);
    }

    private async Task SendToAsync<T>(Guid peerId, string type, T message, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();

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
