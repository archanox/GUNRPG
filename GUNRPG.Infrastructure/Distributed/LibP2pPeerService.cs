using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Multiformats.Address;
using Nethermind.Libp2p.Core;
using Nethermind.Libp2p.Core.Discovery;
using Nethermind.Libp2p.Core.Dto;
using Nethermind.Libp2p.Protocols;

namespace GUNRPG.Infrastructure.Distributed;

/// <summary>
/// Hosted service that starts the libp2p peer, listens for inbound connections,
/// and uses mDNS to discover and connect to other GUNRPG servers on the local network.
/// When a peer connects, <see cref="Libp2pLockstepTransport"/> handles the session and
/// fires <c>OnPeerConnected</c>, which causes the <c>OperatorEventReplicator</c> to sync
/// operator events — making operators created on any server visible from all others.
/// </summary>
public sealed class LibP2pPeerService : IHostedService
{
    private readonly Guid _nodeId;
    private readonly Libp2pLockstepTransport _transport;
    private readonly IPeerFactory _peerFactory;
    private readonly PeerStore _peerStore;
    private readonly MDnsDiscoveryProtocol _mdns;
    private readonly ILogger<LibP2pPeerService> _logger;

    private ILocalPeer? _localPeer;
    private CancellationTokenSource? _cts;

    // Tracks libp2p peer IDs we've already started dialing to prevent duplicate outbound dials.
    private readonly HashSet<string> _dialedPeers = new(StringComparer.Ordinal);
    private readonly object _dialedLock = new();

    public LibP2pPeerService(
        Guid nodeId,
        Libp2pLockstepTransport transport,
        IPeerFactory peerFactory,
        PeerStore peerStore,
        MDnsDiscoveryProtocol mdns,
        ILogger<LibP2pPeerService> logger)
    {
        _nodeId = nodeId;
        _transport = transport;
        _peerFactory = peerFactory;
        _peerStore = peerStore;
        _mdns = mdns;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _cts.Token;

        // Derive a stable Ed25519 seed from the GUNRPG node ID via HKDF so the libp2p
        // peer ID is consistent across restarts and tied to the persisted server_node_id.
        var keyBytes = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: _nodeId.ToByteArray(),
            outputLength: 32,
            salt: Array.Empty<byte>(),
            info: "gunrpg-p2p-identity"u8.ToArray());
        var identity = new Identity(keyBytes, KeyType.Ed25519);

        _localPeer = _peerFactory.Create(identity);
        await _localPeer.StartListenAsync([Multiaddress.Decode("/ip4/0.0.0.0/tcp/0")], ct);

        _logger.LogInformation("[P2P] Listening on {Addresses}",
            string.Join(", ", _localPeer.ListenAddresses));

        _peerStore.OnNewPeer += addrs => OnPeerDiscovered(addrs, ct);

        _ = _mdns.StartDiscoveryAsync(_localPeer.ListenAddresses.ToArray(), ct);

        _logger.LogInformation("[P2P] mDNS peer discovery started");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private void OnPeerDiscovered(Multiaddress[] addrs, CancellationToken ct)
    {
        // Extract the libp2p peer ID from the multiaddress (e.g. /ip4/.../p2p/<id>)
        // to deduplicate discovery events for the same remote peer.
        var addr = addrs.FirstOrDefault(a => a.ToString().Contains("/p2p/"));
        if (addr == null) return;

        var addrStr = addr.ToString();
        var p2pIndex = addrStr.LastIndexOf("/p2p/", StringComparison.Ordinal);
        if (p2pIndex < 0) return;

        var peerId = addrStr[(p2pIndex + 5)..];
        if (string.IsNullOrEmpty(peerId)) return;

        bool isNew;
        lock (_dialedLock)
        {
            isNew = _dialedPeers.Add(peerId);
        }

        if (!isNew) return;

        _ = DialPeerAsync(addrs, ct);
    }

    private async Task DialPeerAsync(Multiaddress[] addrs, CancellationToken ct)
    {
        try
        {
            var session = await _localPeer!.DialAsync(addrs, ct);
            await session.DialAsync<Libp2pLockstepTransport>(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[P2P] Failed to connect to discovered peer");
        }
    }
}
