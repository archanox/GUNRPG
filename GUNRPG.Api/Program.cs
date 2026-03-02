using System.Net;
using System.Text.Json.Serialization;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Distributed;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Distributed;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

builder.Services.AddCombatSessionStore(builder.Configuration);
builder.Services.AddSingleton<IDeterministicCombatEngine, DeterministicCombatEngine>();
builder.Services.AddSingleton<OperatorUpdateHub>();

// Distributed game authority: deterministic lockstep replication via libp2p transport
// Persist server node ID so restarts reuse the same identity
const string nodeIdFileName = "server_node_id";
var serverNodeId = LoadOrCreateNodeId(nodeIdFileName);
builder.Services.AddSingleton<IDeterministicGameEngine, DefaultGameEngine>();
var lockstepTransport = new Libp2pLockstepTransport(serverNodeId);
builder.Services.AddSingleton(lockstepTransport);
builder.Services.AddSingleton<ILockstepTransport>(sp => sp.GetRequiredService<Libp2pLockstepTransport>());
builder.Services.AddSingleton<IGameAuthority>(sp =>
{
    var transport = sp.GetRequiredService<ILockstepTransport>();
    var engine = sp.GetRequiredService<IDeterministicGameEngine>();
    return new DistributedAuthority(serverNodeId, transport, engine);
});

// Register libp2p peer service for server-to-server operator event replication.
// Starts a libp2p listener and mDNS discovery so servers can find each other and
// sync operator events, making operators created on any server visible from all others.
builder.Services.AddLibp2pPeer(lockstepTransport, serverNodeId);

builder.Services.AddSingleton<OperatorEventReplicator>(sp =>
{
    var transport = sp.GetRequiredService<ILockstepTransport>();
    var eventStore = sp.GetRequiredService<IOperatorEventStore>();
    var updateHub = sp.GetRequiredService<OperatorUpdateHub>();
    return new OperatorEventReplicator(serverNodeId, transport, eventStore, updateHub);
});

// Replace the OperatorExfilService registered by AddCombatSessionStore with one that
// includes the distributed replicator and update hub, so there is a single definitive registration.
builder.Services.Replace(ServiceDescriptor.Singleton<OperatorExfilService>(sp =>
{
    var eventStore = sp.GetRequiredService<IOperatorEventStore>();
    var replicator = sp.GetRequiredService<OperatorEventReplicator>();
    var updateHub = sp.GetRequiredService<OperatorUpdateHub>();
    return new OperatorExfilService(eventStore, replicator, updateHub);
}));

builder.Services.AddSingleton<CombatSessionService>(sp =>
{
    var sessionStore = sp.GetRequiredService<ICombatSessionStore>();
    var operatorEventStore = sp.GetRequiredService<IOperatorEventStore>();
    var gameAuthority = sp.GetRequiredService<IGameAuthority>();
    return new CombatSessionService(sessionStore, operatorEventStore, gameAuthority);
});
builder.Services.AddSingleton<OperatorService>(sp =>
{
    var exfilService = sp.GetRequiredService<OperatorExfilService>();
    var sessionService = sp.GetRequiredService<CombatSessionService>();
    var eventStore = sp.GetRequiredService<IOperatorEventStore>();
    var offlineSyncHeadStore = sp.GetRequiredService<IOfflineSyncHeadStore>();
    var combatEngine = sp.GetRequiredService<IDeterministicCombatEngine>();
    return new OperatorService(exfilService, sessionService, eventStore, offlineSyncHeadStore, combatEngine);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();

ServiceDiscovery? mdnsDiscovery = null;
app.Lifetime.ApplicationStarted.Register(() =>
{
    try
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addressesFeature = server.Features.Get<IServerAddressesFeature>();
        var addresses = addressesFeature?.Addresses ?? [];

        // Prefer HTTP for LAN discovery; fall back to HTTPS if that's all that's bound.
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://"))
                   ?? addresses.FirstOrDefault(a => a.StartsWith("https://"));

        if (address != null && Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            var port = (ushort)(uri.Port > 0 ? uri.Port : (uri.Scheme == "https" ? 443 : 80));
            var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
            var profile = new ServiceProfile("GUNRPG Server", "_gunrpg._tcp", port);
            profile.AddProperty("version", version);
            profile.AddProperty("environment", app.Environment.EnvironmentName);
            profile.AddProperty("scheme", uri.Scheme);
            // ServiceProfile derives HostName from the instance name, so "GUNRPG Server"
            // produces "GUNRPG\032Server.gunrpg.local" as the SRV Target — not a valid URI
            // host. The library offers no way to specify a custom hostname at construction
            // time, so we post-mutate the Resources to override the SRV Target and address
            // record Names with the machine's actual hostname. Dns.GetHostName() returns the
            // OS-level hostname (always a valid DNS label); appending ".local" is correct for
            // mDNS (RFC 6762).
            var machineHost = new DomainName(Dns.GetHostName().ToLower() + ".local");
            profile.HostName = machineHost;
            foreach (var rec in profile.Resources)
            {
                if (rec is SRVRecord srvRec) srvRec.Target = machineHost;
                else if (rec is AddressRecord addrRec) addrRec.Name = machineHost;
            }
            mdnsDiscovery = new ServiceDiscovery();
            mdnsDiscovery.Advertise(profile);
            Console.WriteLine($"[mDNS] Advertising _gunrpg._tcp on {uri.Scheme}://:{port}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[mDNS] Advertising failed: {ex.Message}");
    }

    // Eagerly resolve the distributed game authority so transport is ready
    var authority = app.Services.GetRequiredService<IGameAuthority>();
    Console.WriteLine($"[Distributed] Game authority initialized (NodeId={authority.NodeId}, protocol={LockstepProtocol.Id})");

    // Eagerly resolve the operator event replicator so it subscribes to OnPeerConnected
    // before any peers connect, ensuring sync requests are sent on the first connection.
    app.Services.GetRequiredService<OperatorEventReplicator>();
    Console.WriteLine("[Distributed] Operator event replicator initialized");
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    mdnsDiscovery?.Dispose();
});

app.Run();

static Guid LoadOrCreateNodeId(string fileName)
{
    try
    {
        if (File.Exists(fileName) &&
            Guid.TryParse(File.ReadAllText(fileName).Trim(), out var existing))
        {
            return existing;
        }
    }
    catch (IOException ex)
    {
        Console.WriteLine($"[Distributed] Warning: could not read {fileName}: {ex.Message}");
    }

    var id = Guid.NewGuid();
    try
    {
        File.WriteAllText(fileName, id.ToString("D"));
    }
    catch (IOException ex)
    {
        Console.WriteLine($"[Distributed] Warning: could not persist node ID to {fileName}: {ex.Message}");
    }

    return id;
}
