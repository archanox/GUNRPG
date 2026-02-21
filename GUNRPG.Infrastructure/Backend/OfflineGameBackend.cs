using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure.Persistence;

namespace GUNRPG.Infrastructure.Backend;

/// <summary>
/// Offline game backend that uses local LiteDB for operator state.
/// Only available when an operator has been previously infiled from the server.
/// Combat remains interactive and player-driven — this backend handles
/// operator data access, not gameplay execution.
/// </summary>
public sealed class OfflineGameBackend : IGameBackend
{
    private readonly OfflineStore _offlineStore;

    public OfflineGameBackend(OfflineStore offlineStore)
    {
        _offlineStore = offlineStore;
    }

    /// <inheritdoc />
    public Task<OperatorDto?> GetOperatorAsync(string id)
    {
        var infiled = _offlineStore.GetInfiledOperator(id);
        if (infiled == null)
            return Task.FromResult<OperatorDto?>(null);

        var dto = JsonSerializer.Deserialize<OperatorDto>(infiled.SnapshotJson);
        return Task.FromResult<OperatorDto?>(dto);
    }

    /// <inheritdoc />
    public Task<OperatorDto> InfilOperatorAsync(string id)
    {
        // Infil is not available in offline mode - requires server connection
        throw new InvalidOperationException(
            "[OFFLINE] Cannot infil operator while offline. Server connection required.");
    }

    /// <inheritdoc />
    public Task<bool> OperatorExistsAsync(string id)
    {
        var exists = _offlineStore.GetInfiledOperator(id) != null;
        return Task.FromResult(exists);
    }
}
