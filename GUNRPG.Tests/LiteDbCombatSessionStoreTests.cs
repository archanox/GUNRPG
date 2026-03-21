using GUNRPG.Application.Mapping;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;

namespace GUNRPG.Tests;

public class LiteDbCombatSessionStoreTests : IDisposable
{
    private static readonly TimeSpan TestOperationTimeout = TimeSpan.FromSeconds(10);
    private readonly string _tempDbPath;
    private readonly LiteDatabase _database;
    private readonly LiteDbCombatSessionStore _store;

    public LiteDbCombatSessionStoreTests()
    {
        // Create a unique temp file for each test run
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_combat_sessions_{Guid.NewGuid()}.db");
        
        // Create custom mapper for consistency with production
        var mapper = new BsonMapper();
        mapper.EnumAsInteger = false;
        mapper.Entity<CombatSessionSnapshot>().Id(x => x.Id);
        
        _database = new LiteDatabase(_tempDbPath, mapper);
        
        // Apply migrations as would happen in production
        LiteDbMigrations.ApplyMigrations(_database);
        LiteDbMigrations.SetDatabaseSchemaVersion(_database, LiteDbMigrations.CurrentSchemaVersion);
        
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
            OperatorId = snapshot.OperatorId,
            Phase = SessionPhase.Completed,
            TurnNumber = 5,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
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
            OperatorId = Guid.NewGuid(),
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

    [Fact]
    public async Task ConcurrentAccess_MultipleThreads_ThreadSafe()
    {
        // Test actual concurrent access from multiple threads
        var snapshot1 = CreateTestSnapshot();
        var snapshot2 = CreateTestSnapshot();
        var snapshot3 = CreateTestSnapshot();

        // Perform concurrent write operations
        var tasks = new[]
        {
            Task.Run(() => _store.SaveAsync(snapshot1)),
            Task.Run(() => _store.SaveAsync(snapshot2)),
            Task.Run(() => _store.SaveAsync(snapshot3))
        };
        await Task.WhenAll(tasks).WaitAsync(TestOperationTimeout);

        // Verify all writes succeeded and can be read concurrently
        var readTasks = new[]
        {
            Task.Run(() => _store.LoadAsync(snapshot1.Id)),
            Task.Run(() => _store.LoadAsync(snapshot2.Id)),
            Task.Run(() => _store.LoadAsync(snapshot3.Id))
        };
        var results = await Task.WhenAll(readTasks).WaitAsync(TestOperationTimeout);

        Assert.All(results, result => Assert.NotNull(result));
        Assert.Contains(results, r => r!.Id == snapshot1.Id);
        Assert.Contains(results, r => r!.Id == snapshot2.Id);
        Assert.Contains(results, r => r!.Id == snapshot3.Id);
    }

    [Fact]
    public void Migrations_AreAppliedOnStartup()
    {
        // Verify schema version is set
        var schemaVersion = LiteDbMigrations.GetDatabaseSchemaVersion(_database);
        Assert.Equal(LiteDbMigrations.CurrentSchemaVersion, schemaVersion);
    }

    [Fact]
    public async Task Migrations_DoNotAffectExistingData()
    {
        // Save data before migration check
        var snapshot = CreateTestSnapshot();
        await _store.SaveAsync(snapshot);

        // Re-apply migrations (should be idempotent)
        LiteDbMigrations.ApplyMigrations(_database);
        LiteDbMigrations.SetDatabaseSchemaVersion(_database, LiteDbMigrations.CurrentSchemaVersion);

        // Verify data still loads correctly
        var loaded = await _store.LoadAsync(snapshot.Id);
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Id, loaded.Id);
        Assert.Equal(snapshot.TurnNumber, loaded.TurnNumber);
    }

    [Fact]
    public async Task Migrations_UpgradeFromVersion0()
    {
        // Create a database without version set (simulating existing database)
        var testDbPath = Path.Combine(Path.GetTempPath(), $"test_migration_{Guid.NewGuid()}.db");
        try
        {
            // Create custom mapper for consistency with production
            var mapper = new BsonMapper();
            mapper.EnumAsInteger = false;
            mapper.Entity<CombatSessionSnapshot>().Id(x => x.Id);
            
            using var db = new LiteDatabase(testDbPath, mapper);
            var col = db.GetCollection<CombatSessionSnapshot>("combat_sessions");
            
            // Add data without setting version (version 0)
            var snapshot = CreateTestSnapshot();
            col.Upsert(snapshot.Id, snapshot);
            
            // Verify no version is set
            Assert.Equal(0, LiteDbMigrations.GetDatabaseSchemaVersion(db));
            
            // Now apply migrations
            LiteDbMigrations.ApplyMigrations(db);
            LiteDbMigrations.SetDatabaseSchemaVersion(db, LiteDbMigrations.CurrentSchemaVersion);
            
            // Verify version is updated
            Assert.Equal(LiteDbMigrations.CurrentSchemaVersion, LiteDbMigrations.GetDatabaseSchemaVersion(db));
            
            // Verify data still exists and is accessible
            var loaded = col.FindById(snapshot.Id);
            Assert.NotNull(loaded);
            Assert.Equal(snapshot.Id, loaded.Id);
        }
        finally
        {
            if (File.Exists(testDbPath))
                File.Delete(testDbPath);
        }
    }

    [Fact]
    public async Task LoadAsync_WithTamperedFinalHash_ReturnsNull()
    {
        // Create a session with a valid FinalHash.
        var session = CombatSession.CreateDefault(seed: 55, id: Guid.NewGuid());
        var intentsA = new SimultaneousIntents(session.Player.Id) { Primary = PrimaryAction.Fire };
        var intentsB = new SimultaneousIntents(session.Player.Id) { Primary = PrimaryAction.None };
        session.RecordReplayTurn(intentsA);
        session.RecordReplayTurn(intentsB);
        session.TransitionTo(SessionPhase.Completed);

        var snapshot = CreateCompletedSnapshot(session);

        // Save a copy with a single flipped hash byte.
        await _store.SaveAsync(CloneWithTamperedHash(snapshot, session.FinalHash!));

        // The store must reject the tampered session.
        var loaded = await _store.LoadAsync(session.Id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task LoadAsync_WithValidFinalHash_ReturnsSnapshot()
    {
        // Ensure that a completed session with a legitimate FinalHash loads correctly.
        var session = CombatSession.CreateDefault(seed: 99, id: Guid.NewGuid());
        var intentsA = new SimultaneousIntents(session.Player.Id) { Primary = PrimaryAction.Fire };
        session.RecordReplayTurn(intentsA);
        session.TransitionTo(SessionPhase.Completed);

        var snapshot = CreateCompletedSnapshot(session);
        await _store.SaveAsync(snapshot);

        var loaded = await _store.LoadAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(session.Id, loaded!.Id);
        Assert.Equal(SessionPhase.Completed, loaded.Phase);
    }

    /// <summary>
    /// Builds a <see cref="CombatSessionSnapshot"/> with the hash-critical fields taken from a
    /// completed <see cref="CombatSession"/> so that <see cref="CombatSessionHasher"/> validates it.
    /// </summary>
    private static CombatSessionSnapshot CreateCompletedSnapshot(CombatSession session) =>
        new()
        {
            Id = session.Id,
            OperatorId = session.OperatorId.Value,
            Phase = SessionPhase.Completed,
            TurnNumber = session.TurnNumber,
            Combat = new CombatStateSnapshot
            {
                Phase = CombatPhase.Planning,
                CurrentTimeMs = 0,
                RandomState = new RandomStateSnapshot { Seed = session.Seed, CallCount = 0 }
            },
            Player = CreateTestOperator("Player"),
            Enemy = CreateTestOperator("Enemy"),
            Pet = CreateTestPet(),
            EnemyLevel = session.EnemyLevel,
            Seed = session.Seed,
            PostCombatResolved = false,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ReplayTurns = session.ReplayTurns.Select(t => new IntentSnapshot
            {
                OperatorId = t.OperatorId,
                Primary = t.Primary,
                Movement = t.Movement,
                Stance = t.Stance,
                Cover = t.Cover,
                CancelMovement = t.CancelMovement,
                SubmittedAtMs = t.SubmittedAtMs
            }).ToList(),
            Version = session.Version,
            FinalHash = (byte[])session.FinalHash!.Clone(),
        };

    /// <summary>
    /// Returns a copy of <paramref name="snapshot"/> with a single flipped byte in the FinalHash,
    /// simulating tampering while keeping all other fields identical.
    /// </summary>
    private static CombatSessionSnapshot CloneWithTamperedHash(CombatSessionSnapshot snapshot, byte[] originalHash)
    {
        var tamperedHash = (byte[])originalHash.Clone();
        tamperedHash[0] ^= 0xFF;
        return new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            ReplayTurns = snapshot.ReplayTurns,
            Version = snapshot.Version,
            FinalHash = tamperedHash,
        };
    }

    [Fact]
    public async Task LoadAsync_ServiceCreatedSession_WithStateBasedFinalHash_ReturnsSnapshot()
    {
        // Create and complete a session via the service so that:
        // - ReplayInitialSnapshotJson is recorded (service sets it in CreateSessionAsync)
        // - FinalHash is computed from replayed state via FinalizeAsync (state-based)
        // This test exercises the state-based replay validation branch of IsHashValidAsync,
        // ensuring that both the replay hash AND the cached snapshot state pass.
        var store2 = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store2);

        var createResult = await service.CreateSessionAsync(new SessionCreateRequest { Seed = 77 });
        Assert.True(createResult.IsSuccess);
        var sessionId = createResult.Value!.Id;

        await RunSessionToCompletion(service, sessionId);

        // Fetch from the InMemory store (which uses the state-based path too) to get the snapshot
        var inMemorySnapshot = await store2.LoadAsync(sessionId);
        Assert.NotNull(inMemorySnapshot);
        Assert.Equal(SessionPhase.Completed, inMemorySnapshot!.Phase);
        Assert.NotNull(inMemorySnapshot.FinalHash);
        Assert.False(string.IsNullOrEmpty(inMemorySnapshot.ReplayInitialSnapshotJson),
            "ReplayInitialSnapshotJson must be set for the state-based branch to execute");

        // Persist it to the LiteDB store.
        await _store.SaveAsync(inMemorySnapshot);

        // LiteDB LoadAsync must validate it through the state-based replay branch and accept it.
        var loaded = await _store.LoadAsync(sessionId);
        Assert.NotNull(loaded);
        Assert.Equal(sessionId, loaded!.Id);
        Assert.Equal(SessionPhase.Completed, loaded.Phase);
        Assert.NotNull(loaded.FinalHash);
    }

    [Fact]
    public async Task LoadAsync_ServiceCreatedSession_WithTamperedCachedState_ReturnsNull()
    {
        // Persist a service-created (state-based FinalHash) completed session with
        // Player.Health tampered. Even though ReplayTurns and FinalHash are unchanged,
        // the cached-state check (ComputeStateHash(snapshot) vs FinalHash) must reject it.
        var store2 = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store2);

        var createResult = await service.CreateSessionAsync(new SessionCreateRequest { Seed = 43 });
        Assert.True(createResult.IsSuccess);
        var sessionId = createResult.Value!.Id;
        await RunSessionToCompletion(service, sessionId);

        var inMemorySnapshot = await store2.LoadAsync(sessionId);
        Assert.NotNull(inMemorySnapshot);
        Assert.NotNull(inMemorySnapshot!.FinalHash);

        // Tamper with the cached Player.Health while leaving FinalHash intact.
        var tamperedSnapshot = new CombatSessionSnapshot
        {
            Id = inMemorySnapshot.Id,
            OperatorId = inMemorySnapshot.OperatorId,
            Phase = inMemorySnapshot.Phase,
            TurnNumber = inMemorySnapshot.TurnNumber,
            Combat = inMemorySnapshot.Combat,
            Player = inMemorySnapshot.Player == null ? null : new OperatorSnapshot
            {
                Id = inMemorySnapshot.Player.Id,
                Name = inMemorySnapshot.Player.Name,
                Health = inMemorySnapshot.Player.Health + 9999f,   // tampered
                MaxHealth = inMemorySnapshot.Player.MaxHealth,
                Stamina = inMemorySnapshot.Player.Stamina,
                MaxStamina = inMemorySnapshot.Player.MaxStamina,
                MovementState = inMemorySnapshot.Player.MovementState,
                AimState = inMemorySnapshot.Player.AimState,
                WeaponState = inMemorySnapshot.Player.WeaponState,
                CurrentMovement = inMemorySnapshot.Player.CurrentMovement,
                CurrentCover = inMemorySnapshot.Player.CurrentCover,
                CurrentDirection = inMemorySnapshot.Player.CurrentDirection,
                CurrentAmmo = inMemorySnapshot.Player.CurrentAmmo,
                DistanceToOpponent = inMemorySnapshot.Player.DistanceToOpponent,
            },
            Enemy = inMemorySnapshot.Enemy,
            Pet = inMemorySnapshot.Pet,
            EnemyLevel = inMemorySnapshot.EnemyLevel,
            Seed = inMemorySnapshot.Seed,
            PostCombatResolved = inMemorySnapshot.PostCombatResolved,
            CreatedAt = inMemorySnapshot.CreatedAt,
            CompletedAt = inMemorySnapshot.CompletedAt,
            LastActionTimestamp = inMemorySnapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = inMemorySnapshot.ReplayInitialSnapshotJson,
            ReplayTurns = inMemorySnapshot.ReplayTurns,
            Version = inMemorySnapshot.Version,
            FinalHash = (byte[])inMemorySnapshot.FinalHash!.Clone(),
        };

        await _store.SaveAsync(tamperedSnapshot);

        // The store must reject the tampered cached state.
        var loaded = await _store.LoadAsync(sessionId);
        Assert.Null(loaded);
    }

    private static async Task RunSessionToCompletion(CombatSessionService service, Guid sessionId, int maxTurns = 30)
    {
        for (var i = 0; i < maxTurns; i++)
        {
            var stateResult = await service.GetStateAsync(sessionId);
            if (!stateResult.IsSuccess) break;
            if (stateResult.Value!.Phase == SessionPhase.Completed) return;

            await service.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new GUNRPG.Application.Dtos.IntentDto { Primary = PrimaryAction.Fire }
            });

            var advResult = await service.AdvanceAsync(sessionId);
            if (!advResult.IsSuccess) break;
            if (advResult.Value!.Phase == SessionPhase.Completed) return;
        }
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
            OperatorId = Guid.NewGuid(),
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
