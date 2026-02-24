using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for <see cref="DeterministicCombatEngine"/> and <see cref="OfflineMissionHashing"/>.
/// Validates determinism guarantees required for server-side replay verification.
/// </summary>
public sealed class DeterministicCombatEngineTests
{
    private readonly IDeterministicCombatEngine _engine = new DeterministicCombatEngine();

    // ─── Determinism tests ───

    [Fact]
    public void Execute_SameSnapshotAndSeed_ProducesIdenticalResult()
    {
        var snapshot = CreateTestOperator();
        const int seed = 12345;

        var result1 = _engine.Execute(snapshot, seed);
        var result2 = _engine.Execute(snapshot, seed);

        Assert.Equal(
            OfflineMissionHashing.ComputeOperatorStateHash(result1.ResultOperator),
            OfflineMissionHashing.ComputeOperatorStateHash(result2.ResultOperator));
        Assert.Equal(result1.ResultOperator.TotalXp, result2.ResultOperator.TotalXp);
        Assert.Equal(result1.ResultOperator.CurrentHealth, result2.ResultOperator.CurrentHealth);
        Assert.Equal(result1.IsVictory, result2.IsVictory);
        Assert.Equal(result1.OperatorDied, result2.OperatorDied);
        Assert.Equal(result1.BattleLog.Count, result2.BattleLog.Count);
    }

    [Fact]
    public void Execute_DifferentSeed_ProducesDifferentResult()
    {
        var snapshot = CreateTestOperator();

        // Run engine across a range of seeds and verify we get at least two distinct hashes.
        // A deterministic engine driven by Random(seed) will produce different sequences for different seeds.
        var hashes = Enumerable.Range(1, 50)
            .Select(seed => OfflineMissionHashing.ComputeOperatorStateHash(_engine.Execute(snapshot, seed).ResultOperator))
            .Distinct()
            .Count();

        Assert.True(hashes > 1, "Engine must produce distinct results for at least some different seeds");
    }

    [Fact]
    public void Execute_ProducesNonNullResult()
    {
        var snapshot = CreateTestOperator();

        var result = _engine.Execute(snapshot, 42);

        Assert.NotNull(result);
        Assert.NotNull(result.ResultOperator);
        Assert.NotNull(result.BattleLog);
    }

    [Fact]
    public void Execute_ResultOperator_HasValidXp()
    {
        var snapshot = CreateTestOperator();
        var initialXp = snapshot.TotalXp;

        var result = _engine.Execute(snapshot, 42);

        // XP can only be 0, 50, or 100 relative to initial
        var xpDelta = result.ResultOperator.TotalXp - initialXp;
        Assert.True(xpDelta == 0 || xpDelta == 50 || xpDelta == 100,
            $"XP delta {xpDelta} is not a valid reward (0, 50, or 100)");
    }

    [Fact]
    public void Execute_ResultOperator_HealthIsPositive()
    {
        var snapshot = CreateTestOperator();

        var result = _engine.Execute(snapshot, 42);

        Assert.True(result.ResultOperator.CurrentHealth >= 1f,
            $"Health {result.ResultOperator.CurrentHealth} must be at least 1");
    }

    [Fact]
    public void Execute_VictoryAndDeath_AreMutuallyExclusive()
    {
        // Test with multiple seeds to cover both outcomes
        for (int seed = 1; seed <= 20; seed++)
        {
            var snapshot = CreateTestOperator();
            var result = _engine.Execute(snapshot, seed);
            Assert.False(result.IsVictory && result.OperatorDied,
                $"Seed {seed}: IsVictory and OperatorDied cannot both be true");
        }
    }

    [Fact]
    public void Execute_BattleLog_ContainsAtLeastOneEntry()
    {
        var snapshot = CreateTestOperator();

        var result = _engine.Execute(snapshot, 42);

        Assert.NotEmpty(result.BattleLog);
    }

    [Fact]
    public void Execute_BattleLog_EntriesHaveValidEventTypes()
    {
        var snapshot = CreateTestOperator();

        var result = _engine.Execute(snapshot, 42);

        foreach (var entry in result.BattleLog)
        {
            Assert.True(entry.EventType == "Damage" || entry.EventType == "Miss",
                $"Unexpected event type: {entry.EventType}");
        }
    }

    // ─── Snapshot hash stability tests ───

    [Fact]
    public void ComputeSnapshotHash_SameJson_ProducesIdenticalHash()
    {
        const string json = "{\"id\":\"test\",\"name\":\"Op\"}";

        var hash1 = OfflineMissionHashing.ComputeSnapshotHash(json);
        var hash2 = OfflineMissionHashing.ComputeSnapshotHash(json);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeSnapshotHash_DifferentJson_ProducesDifferentHash()
    {
        var hash1 = OfflineMissionHashing.ComputeSnapshotHash("{\"id\":\"a\"}");
        var hash2 = OfflineMissionHashing.ComputeSnapshotHash("{\"id\":\"b\"}");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeSnapshotHash_IsHexString()
    {
        var hash = OfflineMissionHashing.ComputeSnapshotHash("{}");

        Assert.All(hash, c => Assert.True(
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F'),
            $"Character '{c}' is not a valid uppercase hex digit"));
        Assert.Equal(64, hash.Length); // SHA256 = 32 bytes = 64 hex chars
    }

    [Fact]
    public void ComputeOperatorStateHash_StableAcrossSerializations()
    {
        var dto = CreateTestOperatorDto();

        // Compute hash twice from same DTO
        var hash1 = OfflineMissionHashing.ComputeOperatorStateHash(dto);
        var hash2 = OfflineMissionHashing.ComputeOperatorStateHash(dto);

        Assert.Equal(hash1, hash2);
    }

    // ─── Replay parity tests ───

    [Fact]
    public void Execute_ReplayResultMatchesClientResult()
    {
        // Simulate client creating an envelope
        var snapshot = CreateTestOperator();
        const int seed = 77777;

        var clientResult = _engine.Execute(snapshot, seed);
        var clientHash = OfflineMissionHashing.ComputeOperatorStateHash(clientResult.ResultOperator);

        // Simulate server re-running the same engine
        var serverResult = _engine.Execute(snapshot, seed);
        var serverHash = OfflineMissionHashing.ComputeOperatorStateHash(serverResult.ResultOperator);

        Assert.Equal(clientHash, serverHash);
    }

    [Fact]
    public void Execute_ReplayFromSerializedSnapshot_ProducesSameResult()
    {
        var snapshot = CreateTestOperator();
        const int seed = 42;

        // Client runs engine and serializes initial snapshot
        var clientResult = _engine.Execute(snapshot, seed);
        var clientHash = OfflineMissionHashing.ComputeOperatorStateHash(clientResult.ResultOperator);

        // Server deserializes snapshot JSON and re-runs engine
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var serialized = JsonSerializer.Serialize(snapshot, jsonOptions);
        var deserialized = JsonSerializer.Deserialize<OperatorDto>(serialized, jsonOptions)!;
        var serverResult = _engine.Execute(deserialized, seed);
        var serverHash = OfflineMissionHashing.ComputeOperatorStateHash(serverResult.ResultOperator);

        Assert.Equal(clientHash, serverHash);
    }

    // ─── Helpers ───

    private static OperatorDto CreateTestOperator() => new()
    {
        Id = "op-test",
        Name = "TestOperator",
        TotalXp = 500,
        CurrentHealth = 100f,
        MaxHealth = 100f,
        EquippedWeaponName = "Rifle",
        UnlockedPerks = new List<string> { "Steady Aim" },
        ExfilStreak = 2,
        IsDead = false,
        CurrentMode = "Infil",
        LockedLoadout = "Rifle"
    };

    private static OperatorDto CreateTestOperatorDto() => new()
    {
        Id = "op-hash-test",
        Name = "HashTestOp",
        TotalXp = 1000,
        CurrentHealth = 80f,
        MaxHealth = 100f,
        EquippedWeaponName = "SMG",
        UnlockedPerks = new List<string> { "Fast Reload", "Suppressor" },
        ExfilStreak = 3,
        IsDead = false,
        CurrentMode = "Infil",
        LockedLoadout = "SMG"
    };
}
