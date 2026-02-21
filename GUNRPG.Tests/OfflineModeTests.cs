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
    public async Task OfflineBackend_InfilOperator_ThrowsOffline()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.InfilOperatorAsync("op-1"));
    }

    [Fact]
    public async Task OfflineBackend_ExecuteMission_PersistsResult()
    {
        var op = CreateTestOperator("op-1", "TestOp");
        op.TotalXp = 100;
        op.ExfilStreak = 0;
        _offlineStore.SaveInfiledOperator(op);

        var offlineBackend = new OfflineGameBackend(_offlineStore);
        var result = await offlineBackend.ExecuteMissionAsync(new MissionRequest
        {
            OperatorId = "op-1",
            SessionId = "session-1"
        });

        // Result uses real combat simulation — outcome is deterministic but not hardcoded
        Assert.NotNull(result);
        Assert.Equal("op-1", result.OperatorId);
        Assert.True(result.TurnsSurvived > 0, "Combat should have lasted at least one turn");
        Assert.True(result.XpGained >= 0, "XP gained should be non-negative");

        // Verify mission result was persisted
        var unsynced = _offlineStore.GetUnsyncedResults("op-1");
        Assert.Single(unsynced);
        Assert.Contains("executedOffline", unsynced[0].ResultJson);

        // Verify operator snapshot was updated
        var updated = await offlineBackend.GetOperatorAsync("op-1");
        Assert.NotNull(updated);

        if (result.Victory)
        {
            Assert.True(updated.TotalXp > 100, "Victory should grant XP");
            Assert.Equal(1, updated.ExfilStreak);
        }
        else if (result.OperatorDied)
        {
            Assert.True(updated.IsDead, "Operator death should be recorded in snapshot");
        }
    }

    [Fact]
    public async Task OfflineBackend_ExecuteMission_ThrowsWhenNoSnapshot()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => offlineBackend.ExecuteMissionAsync(new MissionRequest { OperatorId = "nonexistent" }));
    }

    [Fact]
    public async Task OfflineBackend_OperatorExists_ChecksInfiledStatus()
    {
        var offlineBackend = new OfflineGameBackend(_offlineStore);

        Assert.False(await offlineBackend.OperatorExistsAsync("op-1"));

        _offlineStore.SaveInfiledOperator(CreateTestOperator("op-1", "TestOp"));

        Assert.True(await offlineBackend.OperatorExistsAsync("op-1"));
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
