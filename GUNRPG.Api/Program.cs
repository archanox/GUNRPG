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
        var httpAddress = addressesFeature?.Addresses.FirstOrDefault(a => a.StartsWith("http://"));
        if (httpAddress != null && Uri.TryCreate(httpAddress, UriKind.Absolute, out var uri))
        {
            var port = (ushort)(uri.Port > 0 ? uri.Port : 80);
            var profile = new ServiceProfile("GUNRPG Server", "_gunrpg._tcp", port);
            profile.AddProperty("version", "0.1.0");
            profile.AddProperty("environment", app.Environment.EnvironmentName);
            mdnsDiscovery = new ServiceDiscovery();
            mdnsDiscovery.Advertise(profile);
            Console.WriteLine($"[mDNS] Advertising _gunrpg._tcp on port {port}");
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
