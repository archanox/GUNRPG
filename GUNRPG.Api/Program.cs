using System.Net;
using System.Text.Json.Serialization;
using Fido2NetLib;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Distributed;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Distributed;
using GUNRPG.Infrastructure.Identity;
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
builder.Services.AddSingleton<CombatSessionUpdateHub>();

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
    var sessionUpdateHub = sp.GetRequiredService<CombatSessionUpdateHub>();
    return new CombatSessionService(sessionStore, operatorEventStore, gameAuthority, sessionUpdateHub);
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

// ── Identity System (WebAuthn + JWT + Device Code Flow) ──────────────────
// Bind Fido2Configuration from appsettings "WebAuthn" section
builder.Services.Configure<Fido2Configuration>(cfg =>
{
    var webAuthnSection = builder.Configuration.GetSection(WebAuthnOptions.SectionName);
    cfg.ServerDomain = webAuthnSection[nameof(WebAuthnOptions.ServerDomain)] ?? "localhost";
    cfg.ServerName = webAuthnSection[nameof(WebAuthnOptions.ServerName)] ?? "GunRPG";
    cfg.Origins = webAuthnSection.GetSection(nameof(WebAuthnOptions.Origins))
        .GetChildren()
        .Select(x => x.Value!)
        .Where(x => x is not null)
        .ToHashSet();
    if (cfg.Origins.Count == 0) cfg.Origins = new HashSet<string> { "https://localhost" };
});
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

// Expand ~ in Kestrel TLS cert paths (e.g. ~/.gunrpg/keys/cert.pem) before Kestrel reads them.
ExpandKestrelCertPaths(builder.Configuration);

// Auto-provision Tailscale TLS certs if the cert/key files don't exist yet
EnsureTailscaleCerts(builder.Configuration);

var verificationUri = builder.Configuration["WebAuthn:VerificationUri"]
    ?? ResolveVerificationUri(builder.Configuration);
builder.Services.AddGunRpgIdentity(verificationUri);

// ── CORS ─────────────────────────────────────────────────────────────────
// Allowed origins are read from WebAuthn:Origins so there is a single source of truth.
// In production every listed origin must be HTTPS (validated at startup by options validation).
var allowedCorsOrigins = builder.Configuration
    .GetSection($"{WebAuthnOptions.SectionName}:{nameof(WebAuthnOptions.Origins)}")
    .GetChildren()
    .Select(x => x.Value)
    .Where(x => x is not null)
    .Cast<string>()
    .ToArray();

if (allowedCorsOrigins.Length == 0)
    allowedCorsOrigins = ["https://localhost"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("GunRpgPolicy", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── JWT Bearer authentication with Ed25519 signature validation ───────────
// Configure after AddGunRpgIdentity so JwtTokenService is registered.
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>,
    GUNRPG.Api.Identity.Ed25519JwtBearerPostConfigure>();
builder.Services.AddAuthorizationBuilder();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors("GunRpgPolicy");
app.UseAuthentication();
app.UseAuthorization();

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
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    mdnsDiscovery?.Dispose();
});

app.Run();

// Expands a leading ~ to the current user's home directory so that config paths
// like ~/.gunrpg/keys/cert.pem work correctly on Linux/macOS.
static string ExpandHomePath(string path)
{
    if (path.StartsWith("~/", StringComparison.Ordinal))
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, path[2..]);
    }
    if (path == "~")
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return path;
}

// Reads the Kestrel HTTPS certificate paths from configuration, expands any leading ~,
// and injects the expanded values back as an in-memory override so that both
// EnsureTailscaleCerts and Kestrel itself see the fully-qualified paths.
static void ExpandKestrelCertPaths(Microsoft.Extensions.Configuration.ConfigurationManager configuration)
{
    const string certKey = "Kestrel:Endpoints:Https:Certificate:Path";
    const string keyKey  = "Kestrel:Endpoints:Https:Certificate:KeyPath";

    var certPath = configuration[certKey];
    var keyPath  = configuration[keyKey];

    var overrides = new Dictionary<string, string?>();
    if (!string.IsNullOrEmpty(certPath))
        overrides[certKey] = ExpandHomePath(certPath);
    if (!string.IsNullOrEmpty(keyPath))
        overrides[keyKey] = ExpandHomePath(keyPath);

    if (overrides.Count > 0)
        configuration.AddInMemoryCollection(overrides);
}

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

// Derives the device-code verification URI by querying the Tailscale CLI for the
// node's DNS name and combining it with the Kestrel HTTPS port.
// Falls back to https://localhost/device when Tailscale is not available.
static string ResolveVerificationUri(IConfiguration configuration)
{
    const string fallback = "https://localhost/device";
    const string verifyPath = "/device";

    var httpsUrl = configuration["Kestrel:Endpoints:Https:Url"];
    var port = 443;
    if (!string.IsNullOrEmpty(httpsUrl))
    {
        // Parse port from the Kestrel URL (e.g. "https://*:7168"); replace wildcards
        // with a valid placeholder so Uri can parse it.
        var normalised = httpsUrl.Replace("*", "localhost", StringComparison.Ordinal)
                                 .Replace("+", "localhost", StringComparison.Ordinal);
        if (Uri.TryCreate(normalised, UriKind.Absolute, out var kestrelUri) && kestrelUri.Port > 0)
            port = kestrelUri.Port;
    }

    var host = TryGetTailscaleDnsName();
    if (string.IsNullOrEmpty(host))
        return fallback;

    return port == 443
        ? $"https://{host}{verifyPath}"
        : $"https://{host}:{port}{verifyPath}";
}

// Queries `tailscale status --json` and returns Self.DNSName (trailing dot stripped).
// Returns null when the Tailscale CLI is not installed, not running, or the output cannot be parsed.
static string? TryGetTailscaleDnsName()
{
    try
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tailscale",
                Arguments = "status --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            Console.WriteLine("[VerificationUri] tailscale process could not be started. Using fallback.");
            return null;
        }

        // Drain both streams concurrently to prevent buffer-full deadlocks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!Task.WhenAll(stdoutTask, stderrTask).Wait(TimeSpan.FromSeconds(5)))
        {
            try { if (!process.HasExited) process.Kill(); } catch { /* may have exited concurrently */ }
            Console.WriteLine("[VerificationUri] tailscale status --json timed out. Using fallback.");
            return null;
        }

        process.WaitForExit(); // Ensure exit code is fully populated

        var json = stdoutTask.Result;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Self", out var self))
            return null;
        if (!self.TryGetProperty("DNSName", out var dnsNameEl))
            return null;

        var dnsName = dnsNameEl.GetString();
        return string.IsNullOrEmpty(dnsName)
            ? null
            : dnsName.TrimEnd('.');  // Tailscale appends a trailing dot (FQDN)
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[VerificationUri] tailscale status --json failed: {ex.Message}. Using fallback.");
        return null;
    }
}

// Provisions TLS cert/key files via `tailscale cert` when they are absent.
// Reads the cert and key paths from the Kestrel config and the domain from tailscale status --json.
// Logs the outcome; never throws — Kestrel will report a clear error on startup if certs are still missing.
static void EnsureTailscaleCerts(IConfiguration configuration)
{
    var certPath = configuration["Kestrel:Endpoints:Https:Certificate:Path"];
    var keyPath = configuration["Kestrel:Endpoints:Https:Certificate:KeyPath"];

    if (string.IsNullOrEmpty(certPath) || string.IsNullOrEmpty(keyPath))
        return; // No Kestrel TLS cert configured; nothing to provision

    if (File.Exists(certPath) && File.Exists(keyPath))
        return; // Certificates already present

    var dnsName = TryGetTailscaleDnsName();
    if (string.IsNullOrEmpty(dnsName))
    {
        Console.WriteLine("[Certs] Tailscale DNS name unavailable; cannot auto-provision certificates.");
        return;
    }

    // Ensure the target directory exists (e.g. ~/.gunrpg/keys/) before invoking tailscale cert.
    var certDir = Path.GetDirectoryName(certPath);
    if (!string.IsNullOrEmpty(certDir) && !Directory.Exists(certDir))
    {
        Directory.CreateDirectory(certDir);
        Console.WriteLine($"[Certs] Created directory {certDir}.");
    }

    Console.WriteLine($"[Certs] Certificate files not found. Provisioning via 'tailscale cert' for '{dnsName}'...");

    try
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tailscale",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("cert");
        process.StartInfo.ArgumentList.Add($"--cert-file={certPath}");
        process.StartInfo.ArgumentList.Add($"--key-file={keyPath}");
        process.StartInfo.ArgumentList.Add(dnsName);

        if (!process.Start())
        {
            Console.WriteLine("[Certs] tailscale cert process could not be started.");
            return;
        }

        // Drain both streams concurrently to prevent buffer-full deadlocks.
        // Waiting on Task.WhenAll guarantees both tasks are complete before we
        // read their results, and ensures the exit code is retrievable.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!Task.WhenAll(stdoutTask, stderrTask).Wait(TimeSpan.FromSeconds(30)))
        {
            try { if (!process.HasExited) process.Kill(); } catch { /* may have exited concurrently */ }
            Console.WriteLine("[Certs] tailscale cert timed out after 30 seconds.");
            return;
        }

        process.WaitForExit(); // Ensure the exit code is fully populated

        if (process.ExitCode == 0)
        {
            Console.WriteLine($"[Certs] Successfully provisioned {certPath} and {keyPath}.");
        }
        else
        {
            Console.WriteLine($"[Certs] tailscale cert failed (exit code {process.ExitCode}): {stderrTask.Result.Trim()}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Certs] tailscale cert failed: {ex.Message}");
    }
}
