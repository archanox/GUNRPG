using System.Net;
using System.Text.RegularExpressions;
using Makaretu.Dns;

/// <summary>
/// Discovers GUNRPG servers on the local network using mDNS.
/// </summary>
internal static class LanDiscoveryService
{
    // Matches DNS master-zone decimal escape sequences, e.g. \032 (space).
    private static readonly Regex DnsEscapeRegex = new(@"\\(\d{3})", RegexOptions.Compiled);

    public record DiscoveredServer(string Hostname, int Port, string? Version, string? Environment, string Scheme = "http");

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

            // Decode DNS master-zone escape sequences (e.g., \032 → space).
            var hostname = DnsEscapeRegex.Replace(
                srv.Target.ToString().TrimEnd('.'),
                m => ((char)int.Parse(m.Groups[1].Value)).ToString());

            // If the decoded hostname is still not valid in a URI (e.g., contains spaces
            // from a server machine whose hostname has special characters), fall back to a
            // routable IPv4 address from the additional A records in the mDNS response.
            if (Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
            {
                var ip = e.Message.AdditionalRecords.OfType<ARecord>()
                    .Select(a => a.Address)
                    .FirstOrDefault(a => !IPAddress.IsLoopback(a) && !a.IsIPv6LinkLocal
                        && !(a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                             && a.GetAddressBytes() is [169, 254, ..]));
                if (ip == null) return;
                hostname = ip.ToString();
            }

            var port = srv.Port;

            lock (discovered)
            {
                if (!discovered.Any(d => d.Hostname == hostname && d.Port == port))
                    discovered.Add(new DiscoveredServer(hostname, port, version, env, scheme));
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
