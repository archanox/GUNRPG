using System.Collections.Frozen;

namespace GUNRPG.Security;

public sealed class RevokedServerIds
{
    private readonly FrozenSet<Guid> _serverIds;

    public static RevokedServerIds Empty { get; } = new([]);

    public RevokedServerIds(IEnumerable<Guid> serverIds)
    {
        ArgumentNullException.ThrowIfNull(serverIds);
        _serverIds = serverIds.ToFrozenSet();
    }

    public bool IsRevoked(Guid serverId) => _serverIds.Contains(serverId);
}
