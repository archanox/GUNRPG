using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;

namespace GUNRPG.Tests;

public class LiteDbCombatSessionStoreTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly LiteDatabase _database;
    private readonly LiteDbCombatSessionStore _store;

    public LiteDbCombatSessionStoreTests()
    {
        // Create a unique temp file for each test run
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_combat_sessions_{Guid.NewGuid()}.db");
        _database = new LiteDatabase(_tempDbPath);
        _store = new LiteDbCombatSessionStore(_database);
    }

    public void Dispose()
    {
        _database?.Dispose();
        if (File.Exists(_tempDbPath))
        {
            File.Delete(_tempDbPath);
        }
    }

    [Fact]
    public async Task SaveAsync_PersistsSnapshot()
    {
        var snapshot = CreateTestSnapshot();

        await _store.SaveAsync(snapshot);

        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Id, loaded.Id);
        Assert.Equal(snapshot.Phase, loaded.Phase);
        Assert.Equal(snapshot.TurnNumber, loaded.TurnNumber);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNull_WhenNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var loaded = await _store.LoadAsync(nonExistentId);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveAsync_UpdatesExistingSnapshot()
    {
        var snapshot = CreateTestSnapshot();
        await _store.SaveAsync(snapshot);

        // Update the snapshot - create a new instance with updated values
        var updatedSnapshot = new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            Phase = SessionPhase.Completed,
            TurnNumber = 5,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            PlayerXp = snapshot.PlayerXp,
            PlayerLevel = snapshot.PlayerLevel,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt
        };
        await _store.SaveAsync(updatedSnapshot);

        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        Assert.Equal(5, loaded.TurnNumber);
        Assert.Equal(SessionPhase.Completed, loaded.Phase);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSnapshot()
    {
        var snapshot = CreateTestSnapshot();
        await _store.SaveAsync(snapshot);

        await _store.DeleteAsync(snapshot.Id);

        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteAsync_DoesNotThrow_WhenNotFound()
    {
        var nonExistentId = Guid.NewGuid();

        var exception = await Record.ExceptionAsync(async () => await _store.DeleteAsync(nonExistentId));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllSnapshots()
    {
        var snapshot1 = CreateTestSnapshot();
        var snapshot2 = CreateTestSnapshot();
        await _store.SaveAsync(snapshot1);
        await _store.SaveAsync(snapshot2);

        var snapshots = await _store.ListAsync();

        Assert.Equal(2, snapshots.Count);
        Assert.Contains(snapshots, s => s.Id == snapshot1.Id);
        Assert.Contains(snapshots, s => s.Id == snapshot2.Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenNoSnapshots()
    {
        var snapshots = await _store.ListAsync();

        Assert.Empty(snapshots);
    }

    [Fact]
    public async Task SaveAsync_PersistsComplexNestedObjects()
    {
        var snapshot = CreateTestSnapshot();

        await _store.SaveAsync(snapshot);

        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        
        // Verify combat state
        Assert.NotNull(loaded.Combat);
        Assert.Equal(snapshot.Combat.Phase, loaded.Combat.Phase);
        Assert.Equal(snapshot.Combat.CurrentTimeMs, loaded.Combat.CurrentTimeMs);
        
        // Verify player operator
        Assert.NotNull(loaded.Player);
        Assert.Equal(snapshot.Player.Name, loaded.Player.Name);
        Assert.Equal(snapshot.Player.Health, loaded.Player.Health);
        Assert.Equal(snapshot.Player.MovementState, loaded.Player.MovementState);
        
        // Verify pet state
        Assert.NotNull(loaded.Pet);
        Assert.Equal(snapshot.Pet.Health, loaded.Pet.Health);
        Assert.Equal(snapshot.Pet.Morale, loaded.Pet.Morale);
    }

    [Fact]
    public async Task SaveAsync_PersistsEnums()
    {
        var snapshot = new CombatSessionSnapshot
        {
            Id = Guid.NewGuid(),
            Phase = SessionPhase.Completed,
            TurnNumber = 1,
            Combat = new CombatStateSnapshot 
            { 
                Phase = CombatPhase.Ended,
                CurrentTimeMs = 1000,
                RandomState = new RandomStateSnapshot { Seed = 42, CallCount = 0 }
            },
            Player = CreateTestOperator("Player"),
            Enemy = CreateTestOperator("Enemy"),
            Pet = CreateTestPet(),
            PlayerXp = 0,
            PlayerLevel = 1,
            EnemyLevel = 1,
            Seed = 123,
            PostCombatResolved = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _store.SaveAsync(snapshot);

        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        Assert.Equal(SessionPhase.Completed, loaded.Phase);
        Assert.Equal(CombatPhase.Ended, loaded.Combat.Phase);
    }

    [Fact]
    public async Task MultipleInstances_CanAccessSameDatabase()
    {
        var snapshot = CreateTestSnapshot();
        await _store.SaveAsync(snapshot);

        // Create a second store instance pointing to the same database
        using var database2 = new LiteDatabase(_tempDbPath);
        var store2 = new LiteDbCombatSessionStore(database2);

        var loaded = await store2.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Id, loaded.Id);
    }

    private static CombatSessionSnapshot CreateTestSnapshot()
    {
        var id = Guid.NewGuid();
        return new CombatSessionSnapshot
        {
            Id = id,
            Phase = SessionPhase.Planning,
            TurnNumber = 1,
            Combat = new CombatStateSnapshot
            {
                Phase = CombatPhase.Planning,
                CurrentTimeMs = 0,
                RandomState = new RandomStateSnapshot { Seed = 123, CallCount = 0 }
            },
            Player = CreateTestOperator("Player"),
            Enemy = CreateTestOperator("Enemy"),
            Pet = CreateTestPet(),
            PlayerXp = 0,
            PlayerLevel = 1,
            EnemyLevel = 1,
            Seed = 123,
            PostCombatResolved = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static OperatorSnapshot CreateTestOperator(string name)
    {
        return new OperatorSnapshot
        {
            Id = Guid.NewGuid(),
            Name = name,
            Health = 100f,
            MaxHealth = 100f,
            Stamina = 100f,
            MaxStamina = 100f,
            MovementState = MovementState.Stationary,
            AimState = AimState.Hip,
            WeaponState = WeaponState.Ready,
            CurrentMovement = MovementState.Stationary,
            CurrentCover = CoverState.None,
            CurrentDirection = MovementDirection.Holding,
            CurrentAmmo = 30,
            DistanceToOpponent = 10f
        };
    }

    private static PetStateSnapshot CreateTestPet()
    {
        return new PetStateSnapshot
        {
            OperatorId = Guid.NewGuid(),
            Health = 100f,
            Morale = 75f,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }
}
