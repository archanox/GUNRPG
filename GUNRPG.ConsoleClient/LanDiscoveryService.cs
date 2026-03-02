using System.Net;
using Makaretu.Dns;

/// <summary>
/// Discovers GUNRPG servers on the local network using mDNS.
/// </summary>
internal static class LanDiscoveryService
{
    public record DiscoveredServer(string DisplayName, string Hostname, int Port, string? Version, string? Environment, string Scheme = "http");

    /// <summary>
    /// Queries for _gunrpg._tcp service instances on the LAN and returns any found within the given timeout.
    /// </summary>
    public static async Task<List<DiscoveredServer>> DiscoverAsync(TimeSpan timeout)
    {
        var discovered = new List<DiscoveredServer>();
        using var cts = new CancellationTokenSource(timeout);
        using var mdns = new MulticastService();
        using var sd = new ServiceDiscovery(mdns);

        sd.ServiceInstanceDiscovered += (_, e) =>
        {
            if (cts.IsCancellationRequested) return;

            // Filter to only _gunrpg._tcp service instances; the mDNS multicast can
            // surface responses from unrelated services on the same network.
            var instLabels = e.ServiceInstanceName.Labels;
            if (!instLabels.Any(l => string.Equals(l, "_gunrpg", StringComparison.OrdinalIgnoreCase)))
                return;

            var srv = e.Message.Answers.OfType<SRVRecord>().FirstOrDefault()
                   ?? e.Message.AdditionalRecords.OfType<SRVRecord>().FirstOrDefault();
            if (srv == null) return;

            var txt = e.Message.Answers.OfType<TXTRecord>().FirstOrDefault()
                   ?? e.Message.AdditionalRecords.OfType<TXTRecord>().FirstOrDefault();

            var version = txt?.Strings
                .FirstOrDefault(s => s.StartsWith("version=", StringComparison.Ordinal))
                ?["version=".Length..];
            var env = txt?.Strings
                .FirstOrDefault(s => s.StartsWith("environment=", StringComparison.Ordinal))
                ?["environment=".Length..];
            var scheme = txt?.Strings
                .FirstOrDefault(s => s.StartsWith("scheme=", StringComparison.Ordinal))
                ?["scheme=".Length..] ?? "http";

            // DomainName.Labels gives us the decoded label strings without DNS escape
            // sequences — no regex needed. Strip the trailing empty root label if present.
            var hostname = string.Join(".", srv.Target.Labels.Where(l => !string.IsNullOrEmpty(l)));

            // If the decoded hostname is still not valid in a URI (e.g., the SRV target
            // was derived from an instance name containing spaces), fall back to a routable
            // address from the A/AAAA records included in the mDNS response. Search both
            // the Answers and AdditionalRecords sections in case the responder places them
            // in either section.
            if (Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
            {
                var ip = e.Message.Answers.Concat(e.Message.AdditionalRecords)
                    .OfType<AddressRecord>()
                    .Select(r => r.Address)
                    .FirstOrDefault(a =>
                        !IPAddress.IsLoopback(a)
                        && !(a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && a.IsIPv6LinkLocal)
                        && !(a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                             && a.GetAddressBytes() is [169, 254, ..]));
                if (ip == null) return;
                hostname = ip.ToString();
            }

            // Human-readable display name from the first label of the service instance name
            // (e.g., "GUNRPG Server" from "GUNRPG Server._gunrpg._tcp.local").
            var displayName = e.ServiceInstanceName.Labels.FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "Unknown Server";

            var port = srv.Port;

            lock (discovered)
            {
                if (!discovered.Any(d => d.Hostname == hostname && d.Port == port))
                    discovered.Add(new DiscoveredServer(displayName, hostname, port, version, env, scheme));
            }
        };

        try
        {
            mdns.Start();
            sd.QueryServiceInstances("_gunrpg._tcp");

            try
            {
                await Task.Delay(timeout, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout expired normally — discovery window has closed.
            }
        }
        finally
        {
            mdns.Stop();
        }
        return discovered;
    }
}
