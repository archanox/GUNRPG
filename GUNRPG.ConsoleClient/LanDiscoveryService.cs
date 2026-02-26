using Makaretu.Dns;

/// <summary>
/// Discovers GUNRPG servers on the local network using mDNS.
/// </summary>
internal static class LanDiscoveryService
{
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

            var hostname = srv.Target.ToString().TrimEnd('.');
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
