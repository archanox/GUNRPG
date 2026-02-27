using System.Net;
using System.Text.Json.Serialization;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure;
using Makaretu.Dns;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
builder.Services.AddSingleton<CombatSessionService>(sp =>
{
    var sessionStore = sp.GetRequiredService<ICombatSessionStore>();
    var operatorEventStore = sp.GetRequiredService<IOperatorEventStore>();
    return new CombatSessionService(sessionStore, operatorEventStore);
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
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    mdnsDiscovery?.Dispose();
});

app.Run();
