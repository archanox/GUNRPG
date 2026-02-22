using GUNRPG.Application.Backend;
using GUNRPG.Infrastructure;
using GUNRPG.Infrastructure.Backend;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for OfflineStore, OfflineGameBackend, and GameBackendResolver.
/// </summary>
public sealed class OfflineModeTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly OfflineStore _offlineStore;

    public OfflineModeTests()
    {
        _database = new LiteDatabase(":memory:");
        _offlineStore = new OfflineStore(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    // ─── OfflineStore Tests ───

    [Fact]
    public void SaveInfiledOperator_PersistsSnapshot()
    {
        var op = CreateTestOperator("op-1", "TestOperator");

        _offlineStore.SaveInfiledOperator(op);

        var result = _offlineStore.GetInfiledOperator("op-1");
        Assert.NotNull(result);
        Assert.Equal("op-1", result.Id);
        Assert.True(result.IsActive);
        Assert.Contains("TestOperator", result.SnapshotJson);
    }

    [Fact]
    public void SaveInfiledOperator_DeactivatesPreviousActive()
    {
        var op1 = CreateTestOperator("op-1", "First");
        var op2 = CreateTestOperator("op-2", "Second");

        _offlineStore.SaveInfiledOperator(op1);
        _offlineStore.SaveInfiledOperator(op2);

        var first = _offlineStore.GetInfiledOperator("op-1");
        var second = _offlineStore.GetInfiledOperator("op-2");

        Assert.NotNull(first);
        Assert.False(first.IsActive);
        Assert.NotNull(second);
        Assert.True(second.IsActive);
    }

    [Fact]
    public void HasActiveInfiledOperator_ReturnsTrueWhenActive()
    {
        Assert.False(_offlineStore.HasActiveInfiledOperator());

        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "Test"));

        Assert.True(_offlineStore.HasActiveInfiledOperator());
    }

    [Fact]
    public void GetActiveInfiledOperator_ReturnsActiveOnly()
    {
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "First"));
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-2", "Second"));

        var active = _offlineStore.GetActiveInfiledOperator();
        Assert.NotNull(active);
        Assert.Equal("op-2", active.Id);
    }

    [Fact]
    public void RemoveInfiledOperator_DeletesSnapshot()
    {
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "Test"));
        Assert.True(_offlineStore.HasActiveInfiledOperator());

        _offlineStore.RemoveInfiledOperator("op-1");

        Assert.False(_offlineStore.HasActiveInfiledOperator());
        Assert.Null(_offlineStore.GetInfiledOperator("op-1"));
    }

    [Fact]
    public void UpdateOperatorSnapshot_ModifiesExistingSnapshot()
    {
        var op = CreateTestOperator("op-1", "Test");
        _offlineStore.SaveInfiledOperator(op);

        op.TotalXp = 999;
        _offlineStore.UpdateOperatorSnapshot("op-1", op);

        var infiled = _offlineStore.GetInfiledOperator("op-1");
        Assert.NotNull(infiled);
        Assert.Contains("999", infiled.SnapshotJson);
    }

    [Fact]
    public void SaveMissionResult_PersistsResult()
    {
        var result = new OfflineMissionResult
        {
            OperatorId = "op-1",
            ResultJson = "{\"victory\":true}",
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        };

        _offlineStore.SaveMissionResult(result);

        var unsynced = _offlineStore.GetUnsyncedResults("op-1");
        Assert.Single(unsynced);
        Assert.Equal("op-1", unsynced[0].OperatorId);
        Assert.False(unsynced[0].Synced);
    }

    [Fact]
    public void MarkResultSynced_UpdatesSyncedFlag()
    {
        var result = new OfflineMissionResult
        {
            OperatorId = "op-1",
            ResultJson = "{}",
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        };

        _offlineStore.SaveMissionResult(result);

        var unsynced = _offlineStore.GetUnsyncedResults("op-1");
        Assert.Single(unsynced);

        _offlineStore.MarkResultSynced(unsynced[0].Id);

        var afterSync = _offlineStore.GetUnsyncedResults("op-1");
        Assert.Empty(afterSync);
    }

    [Fact]
    public void GetAllUnsyncedResults_ReturnsAllOperators()
    {
        _offlineStore.SaveMissionResult(new OfflineMissionResult
        {
            OperatorId = "op-1",
            ResultJson = "{}",
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        });
        _offlineStore.SaveMissionResult(new OfflineMissionResult
        {
            OperatorId = "op-2",
            ResultJson = "{}",
            ExecutedUtc = DateTime.UtcNow,
            Synced = false
        });

        var all = _offlineStore.GetAllUnsyncedResults();
        Assert.Equal(2, all.Count);
    }

    // ─── OfflineGameBackend Tests ───

    [Fact]
    public async Task OfflineBackend_GetOperator_ReturnsInfiledOperator()
    {
        var op = CreateTestOperator("op-1", "TestOp");
        _offlineStore.SaveInfiledOperator(op);

        var offlineBackend = new OfflineGameBackend(_offlineStore);
        var result = await offlineBackend.GetOperatorAsync("op-1");

        Assert.NotNull(result);
        Assert.Equal("op-1", result.Id);
        Assert.Equal("TestOp", result.Name);
    }

    [Fact]
    public async Task OfflineBackend_GetOperator_ReturnsNullWhenNotInfiled()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);
        var result = await offlineBackend.GetOperatorAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task OfflineBackend_GetOperator_ReturnsNullForInactiveSnapshot()
    {
        // Infil two operators — first one becomes inactive when second is infiled
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "First"));
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-2", "Second"));

        var offlineBackend = new OfflineGameBackend(_offlineStore);

        // Inactive operator should not be accessible
        var inactive = await offlineBackend.GetOperatorAsync("op-1");
        Assert.Null(inactive);

        // Active operator should be accessible
        var active = await offlineBackend.GetOperatorAsync("op-2");
        Assert.NotNull(active);
        Assert.Equal("Second", active.Name);
    }

    [Fact]
    public async Task OfflineBackend_GetOperator_ThrowsOnCorruptedSnapshot()
    {
        // Manually insert a snapshot with invalid JSON
        var corrupted = new InfiledOperator
        {
            Id = "op-corrupt",
            SnapshotJson = "not valid json{{{",
            InfiledUtc = DateTime.UtcNow,
            IsActive = true
        };
        // Use store internals to insert directly
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-corrupt", "Test"));
        // Corrupt the snapshot manually via the database
        var col = _database.GetCollection<InfiledOperator>("infiled_operators");
        var record = col.FindById("op-corrupt");
        record!.SnapshotJson = "not valid json{{{";
        col.Update(record);

        var offlineBackend = new OfflineGameBackend(_offlineStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.GetOperatorAsync("op-corrupt"));
        Assert.Contains("re-infil", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OfflineBackend_InfilOperator_ThrowsOffline()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.InfilOperatorAsync("op-1"));
    }

    [Fact]
    public async Task OfflineBackend_OperatorExists_ChecksActiveInfiledStatus()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        Assert.False(await offlineBackend.OperatorExistsAsync("op-1"));

        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "TestOp"));

        Assert.True(await offlineBackend.OperatorExistsAsync("op-1"));

        // Infiling a second operator deactivates the first
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-2", "TestOp2"));

        Assert.False(await offlineBackend.OperatorExistsAsync("op-1"));
        Assert.True(await offlineBackend.OperatorExistsAsync("op-2"));
    }

    // ─── GameBackendResolver Tests ───

    [Fact]
    public async Task Resolver_WithNoServerAndNoOperator_ReturnsBlockedMode()
    {
        // Use an unreachable URL
        using var unreachableClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var resolver = new GameBackendResolver(unreachableClient, _offlineStore);

        var backend = await resolver.ResolveAsync();

        Assert.Equal(GameMode.Blocked, resolver.CurrentMode);
        // Backend is returned (OnlineGameBackend) but gameplay should be blocked at UI level
        Assert.NotNull(backend);
    }

    [Fact]
    public async Task Resolver_WithNoServerButInfiledOperator_ReturnsOfflineMode()
    {
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "TestOp"));

        using var unreachableClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var resolver = new GameBackendResolver(unreachableClient, _offlineStore);

        var backend = await resolver.ResolveAsync();

        Assert.Equal(GameMode.Offline, resolver.CurrentMode);
        Assert.IsType<OfflineGameBackend>(backend);
    }

    // ─── Guardrail Tests ───

    [Fact]
    public async Task OfflineBackend_InfilBlocked_ThrowsInvalidOperation()
    {
        // Infil must always be blocked when offline — requires server connection
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.InfilOperatorAsync("any-id"));
        Assert.Contains("offline", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OfflineBackend_NeverMakesHttpCalls()
    {
        // OfflineGameBackend only depends on OfflineStore, not HttpClient.
        // Verify it can be constructed and used without any HTTP infrastructure.
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        // All operations should work purely against local store (no HttpClient needed)
        var existsResult = await offlineBackend.OperatorExistsAsync("nonexistent");
        Assert.False(existsResult);

        var getResult = await offlineBackend.GetOperatorAsync("nonexistent");
        Assert.Null(getResult);
    }

    [Fact]
    public async Task OfflineBackend_OperatorCreation_NotSupported()
    {
        // OfflineGameBackend has no CreateOperator method — creation is only possible
        // through the online API. Verify the interface doesn't expose creation.
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        // Trying to infil a non-existent operator offline should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.InfilOperatorAsync("new-op"));
    }

    [Fact]
    public async Task Resolver_Blocked_WhenServerUnreachableAndNoInfiledOperator()
    {
        // Fresh state: no infiled operators
        Assert.False(_offlineStore.HasActiveInfiledOperator());

        using var unreachableClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var resolver = new GameBackendResolver(unreachableClient, _offlineStore);

        var backend = await resolver.ResolveAsync();

        Assert.Equal(GameMode.Blocked, resolver.CurrentMode);
        // Should NOT be OfflineGameBackend — there's nothing to play offline with
        Assert.IsNotType<OfflineGameBackend>(backend);
    }

    [Fact]
    public async Task Resolver_Blocked_WhenAllSnapshotsInactive()
    {
        // Infil two operators — first becomes inactive when second is saved
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "First"));
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-2", "Second"));

        // Remove both — no active snapshot remains
        _offlineStore.RemoveInfiledOperator("op-1");
        _offlineStore.RemoveInfiledOperator("op-2");

        Assert.False(_offlineStore.HasActiveInfiledOperator());

        using var unreachableClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var resolver = new GameBackendResolver(unreachableClient, _offlineStore);

        var result = await resolver.ResolveAsync();

        // Offline readiness requires an ACTIVE snapshot; none here → Blocked
        Assert.Equal(GameMode.Blocked, resolver.CurrentMode);
        Assert.IsNotType<OfflineGameBackend>(result);
    }

    [Fact]
    public async Task OfflineBackend_SingleInfilPath_InfilAlwaysThrows()
    {
        // OfflineGameBackend must never permit infil — it has no server connection.
        // The only valid infil path is through OnlineGameBackend (server reachable).
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.InfilOperatorAsync("any-operator-id"));

        // Message must guide the user back to online infil
        Assert.Contains("offline", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolver_OfflineOnlyWhenActiveSnapshotExists()
    {
        // Infil one operator (it becomes active)
        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-active", "ActiveOp"));
        Assert.True(_offlineStore.HasActiveInfiledOperator());

        using var unreachableClient = new HttpClient { BaseAddress = new Uri("http://localhost:1") };
        var resolver = new GameBackendResolver(unreachableClient, _offlineStore);

        var result = await resolver.ResolveAsync();

        // Active snapshot exists → Offline mode with OfflineGameBackend
        Assert.Equal(GameMode.Offline, resolver.CurrentMode);
        Assert.IsType<OfflineGameBackend>(result);
    }

    // ─── Helpers ───

    private static OperatorDto CreateTestOperator(string id, string name)
    {
        return new OperatorDto
        {
            Id = id,
            Name = name,
            TotalXp = 0,
            CurrentHealth = 100,
            MaxHealth = 100,
            EquippedWeaponName = "TestWeapon",
            UnlockedPerks = new List<string>(),
            ExfilStreak = 0,
            IsDead = false,
            CurrentMode = "Base"
        };
    }
}
