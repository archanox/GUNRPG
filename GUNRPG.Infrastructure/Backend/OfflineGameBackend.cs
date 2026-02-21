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
        if (infiled == null || !infiled.IsActive)
            return Task.FromResult<OperatorDto?>(null);

        try
        {
            var dto = JsonSerializer.Deserialize<OperatorDto>(infiled.SnapshotJson);
            return Task.FromResult<OperatorDto?>(dto);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "[OFFLINE] Stored operator snapshot is invalid or incompatible. " +
                "Please reconnect and re-infil the operator from the server.",
                ex);
        }
    }

    /// <inheritdoc />
    public Task<OperatorDto> InfilOperatorAsync(string id)
    {
        // Infil is not available in offline mode - requires server connection
        throw new InvalidOperationException(
            "[OFFLINE] Cannot infil operator while offline. Server connection required.");
    }

    /// <inheritdoc />
    public async Task<bool> OperatorExistsAsync(string id)
    {
        var op = await GetOperatorAsync(id);
        return op != null;
    }
}
