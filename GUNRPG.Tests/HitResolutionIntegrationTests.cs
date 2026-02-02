using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;
using Xunit.Abstractions;

namespace GUNRPG.Tests;

/// <summary>
/// Integration tests for the complete hit resolution system.
/// Validates that the system works end-to-end with operators and combat.
/// </summary>
public class HitResolutionIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public HitResolutionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CombatSystem_UsesNewHitResolution()
    {
        // Arrange: Create operators with known accuracy
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Accuracy = 0.9f,  // High accuracy
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };

        var enemy = new Operator("Enemy")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Accuracy = 0.9f,  // High accuracy
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };

        var combat = new CombatSystemV2(player, enemy, seed: 42);

        // Act: Execute a single round
        var playerIntents = new SimultaneousIntents(player.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        };

        var enemyIntents = new SimultaneousIntents(enemy.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.Stand
        };

        combat.SubmitIntents(player, playerIntents);
        combat.SubmitIntents(enemy, enemyIntents);
        combat.BeginExecution();
        combat.ExecuteUntilReactionWindow();

        // Assert: With high accuracy, at least one hit should occur
        bool someoneWasHit = player.Health < player.MaxHealth || enemy.Health < enemy.MaxHealth;
        Assert.True(someoneWasHit, "With 90% accuracy, at least one operator should be hit");
        
        _output.WriteLine($"Player Health: {player.Health}/{player.MaxHealth}");
        _output.WriteLine($"Enemy Health: {enemy.Health}/{enemy.MaxHealth}");
    }

    [Fact]
    public void AccuracyAffectsHitRate()
    {
        // Test multiple rounds with different accuracy levels
        var testCases = new[]
        {
            (accuracy: 0.95f, expectedMinHitRate: 0.5f, label: "High accuracy"),
            (accuracy: 0.5f, expectedMinHitRate: 0.2f, label: "Medium accuracy"),
            (accuracy: 0.2f, expectedMinHitRate: 0.0f, label: "Low accuracy")
        };

        foreach (var (accuracy, expectedMinHitRate, label) in testCases)
        {
            int totalRounds = 50;
            int hitsLanded = 0;

            for (int i = 0; i < totalRounds; i++)
            {
                var player = new Operator("Player")
                {
                    EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                    CurrentAmmo = 30,
                    DistanceToOpponent = 15f,
                    Accuracy = accuracy,
                    AccuracyProficiency = 1.0f  // Max proficiency to isolate accuracy stat effect
                };

                var enemy = new Operator("Enemy")
                {
                    EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                    CurrentAmmo = 30,
                    DistanceToOpponent = 15f,
                    Accuracy = 0f,  // Minimum accuracy (0% = maximum aim error)
                    AccuracyProficiency = 0f  // Min proficiency so enemy misses more
                };

                var combat = new CombatSystemV2(player, enemy, seed: 100 + i);

                var playerIntents = new SimultaneousIntents(player.Id)
                {
                    Primary = PrimaryAction.Fire,
                    Movement = MovementAction.Stand
                };

                var enemyIntents = new SimultaneousIntents(enemy.Id)
                {
                    Primary = PrimaryAction.Fire,
                    Movement = MovementAction.Stand
                };

                combat.SubmitIntents(player, playerIntents);
                combat.SubmitIntents(enemy, enemyIntents);
                combat.BeginExecution();
                combat.ExecuteUntilReactionWindow();

                if (enemy.Health < enemy.MaxHealth)
                    hitsLanded++;
            }

            float hitRate = (float)hitsLanded / totalRounds;
            _output.WriteLine($"{label} (Accuracy {accuracy:P0}): {hitsLanded}/{totalRounds} hits = {hitRate:P1}");

            Assert.True(hitRate >= expectedMinHitRate,
                $"{label}: Hit rate {hitRate:P1} should be >= {expectedMinHitRate:P1}");
        }
    }

    [Fact]
    public void RecoilAccumulates_AcrossMultipleShots()
    {
        // Arrange: Operator fires multiple shots
        // Note: This test manually simulates recoil accumulation to test the isolated
        // HitResolution behavior. The actual combat system handles recoil in CombatEvents.cs.
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 5,
            DistanceToOpponent = 15f,
            Accuracy = 1.0f,  // Perfect accuracy to isolate recoil effect (aim error = 0)
            AccuracyProficiency = 0.5f,  // Explicit proficiency
            CurrentRecoilY = 0f
        };

        float weaponRecoil = player.EquippedWeapon.VerticalRecoil;
        _output.WriteLine($"Weapon vertical recoil: {weaponRecoil}");

        var random = new Random(42);

        // Shot 1
        var result1 = HitResolution.ResolveShot(
            BodyPart.UpperTorso, player.Accuracy, weaponRecoil,
            player.CurrentRecoilY, 0f, random);
        player.CurrentRecoilY += weaponRecoil;

        // Shot 2 - should have more recoil
        var result2 = HitResolution.ResolveShot(
            BodyPart.UpperTorso, player.Accuracy, weaponRecoil,
            player.CurrentRecoilY, 0f, random);
        player.CurrentRecoilY += weaponRecoil;

        // Shot 3 - even more recoil
        var result3 = HitResolution.ResolveShot(
            BodyPart.UpperTorso, player.Accuracy, weaponRecoil,
            player.CurrentRecoilY, 0f, random);

        _output.WriteLine($"Shot 1 angle: {result1.FinalAngleDegrees:F4}° -> {result1.HitLocation}");
        _output.WriteLine($"Shot 2 angle: {result2.FinalAngleDegrees:F4}° -> {result2.HitLocation}");
        _output.WriteLine($"Shot 3 angle: {result3.FinalAngleDegrees:F4}° -> {result3.HitLocation}");

        // Assert: Accumulated recoil should affect shot placement
        // With perfect accuracy (1.0), aim error is deterministic (0), so angles should strictly increase
        Assert.True(result2.FinalAngleDegrees > result1.FinalAngleDegrees,
            "Second shot should have higher angle due to recoil accumulation");
        Assert.True(result3.FinalAngleDegrees > result2.FinalAngleDegrees,
            "Third shot should have higher angle due to accumulated recoil");
    }

    [Fact]
    public void BodyPartHits_DealCorrectDamage()
    {
        // Arrange: Create operator with known weapon
        var player = new Operator("Player")
        {
            EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
            CurrentAmmo = 30,
            DistanceToOpponent = 15f,
            Accuracy = 1.0f,  // Perfect accuracy
            AccuracyProficiency = 0.5f  // Explicit proficiency
        };

        var weapon = player.EquippedWeapon;

        // Get expected damage for each body part at this distance
        float headDamage = weapon.GetDamageAtDistance(15f, BodyPart.Head);
        float neckDamage = weapon.GetDamageAtDistance(15f, BodyPart.Neck);
        float upperTorsoDamage = weapon.GetDamageAtDistance(15f, BodyPart.UpperTorso);
        float lowerTorsoDamage = weapon.GetDamageAtDistance(15f, BodyPart.LowerTorso);

        _output.WriteLine($"Expected damage at 15m:");
        _output.WriteLine($"  Head: {headDamage}");
        _output.WriteLine($"  Neck: {neckDamage}");
        _output.WriteLine($"  UpperTorso: {upperTorsoDamage}");
        _output.WriteLine($"  LowerTorso: {lowerTorsoDamage}");

        // Assert: Headshots should deal more damage than body shots
        Assert.True(headDamage > upperTorsoDamage, "Head should deal more damage than torso");
        Assert.True(headDamage > neckDamage || Math.Abs(headDamage - neckDamage) < 0.1f, 
            "Head should deal at least as much damage as neck");
    }

    [Fact]
    public void TargetInFullCover_CannotBeHit_WhenNotFiring()
    {
        // Test that a target in full cover who is NOT firing cannot be hit
        int totalRounds = 50;
        int hitsLanded = 0;

        for (int i = 0; i < totalRounds; i++)
        {
            var player = new Operator("Player")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f,
                Accuracy = 1.0f,  // Perfect accuracy - should hit every time without cover
                AccuracyProficiency = 1.0f
            };

            var enemy = new Operator("Enemy")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 0,  // No ammo - won't be firing
                DistanceToOpponent = 15f,
                Accuracy = 0f,
                AccuracyProficiency = 0f
            };

            // Enemy enters full cover
            enemy.EnterCover(CoverState.Full, 0);
            Assert.Equal(CoverState.Full, enemy.CurrentCover);

            var combat = new CombatSystemV2(player, enemy, seed: 200 + i);

            var playerIntents = new SimultaneousIntents(player.Id)
            {
                Primary = PrimaryAction.Fire,
                Movement = MovementAction.Stand
            };

            var enemyIntents = new SimultaneousIntents(enemy.Id)
            {
                Primary = PrimaryAction.None,  // Not firing
                Movement = MovementAction.Stand
            };

            combat.SubmitIntents(player, playerIntents);
            combat.SubmitIntents(enemy, enemyIntents);
            combat.BeginExecution();
            combat.ExecuteUntilReactionWindow();
            
            // Verify enemy is not actively firing (since they have no ammo)
            Assert.False(enemy.IsActivelyFiring, "Enemy should not be actively firing without ammo");

            if (enemy.Health < enemy.MaxHealth)
                hitsLanded++;
        }

        _output.WriteLine($"Target in full cover (not firing): {hitsLanded}/{totalRounds} hits landed");
        
        // With full cover and not firing, NO hits should land
        Assert.Equal(0, hitsLanded);
    }

    [Fact]
    public void TargetInPartialCover_CanBeHit_WhenExposed()
    {
        // Test that a target in partial cover (peeking) can be hit in exposed body parts
        int totalRounds = 50;
        int hitsLanded = 0;

        for (int i = 0; i < totalRounds; i++)
        {
            var player = new Operator("Player")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f,
                Accuracy = 1.0f,  // Perfect accuracy
                AccuracyProficiency = 1.0f
            };

            var enemy = new Operator("Enemy")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 30,  // Has ammo - can fire from partial cover
                DistanceToOpponent = 15f,
                Accuracy = 0f,
                AccuracyProficiency = 0f
            };

            // Enemy enters partial cover (peeking)
            enemy.EnterCover(CoverState.Partial, 0);
            Assert.Equal(CoverState.Partial, enemy.CurrentCover);

            var combat = new CombatSystemV2(player, enemy, seed: 300 + i);

            var playerIntents = new SimultaneousIntents(player.Id)
            {
                Primary = PrimaryAction.Fire,
                Movement = MovementAction.Stand
            };

            var enemyIntents = new SimultaneousIntents(enemy.Id)
            {
                Primary = PrimaryAction.Fire,  // Firing from partial cover
                Movement = MovementAction.Stand
            };

            combat.SubmitIntents(player, playerIntents);
            combat.SubmitIntents(enemy, enemyIntents);
            combat.BeginExecution();
            combat.ExecuteUntilReactionWindow();
            
            // Verify enemy is actively firing
            Assert.True(enemy.IsActivelyFiring, "Enemy should be actively firing with ammo");

            if (enemy.Health < enemy.MaxHealth)
                hitsLanded++;
        }

        _output.WriteLine($"Target in partial cover (peeking): {hitsLanded}/{totalRounds} hits landed");
        
        // With partial cover (peeking), hits should land on exposed upper body parts
        // With perfect accuracy, we expect a reasonable hit rate when peeking
        Assert.True(hitsLanded > 20, $"Target in partial cover should be hit frequently with perfect accuracy (expected >20, got {hitsLanded})");
    }

    [Fact]
    public void TargetInFullCover_CannotBeHit()
    {
        // Test that a target in full cover (completely concealed) cannot be hit
        int totalRounds = 50;
        int hitsLanded = 0;

        for (int i = 0; i < totalRounds; i++)
        {
            var player = new Operator("Player")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f,
                Accuracy = 1.0f,  // Perfect accuracy
                AccuracyProficiency = 1.0f
            };

            var enemy = new Operator("Enemy")
            {
                EquippedWeapon = WeaponFactory.CreateSturmwolf45(),
                CurrentAmmo = 30,
                DistanceToOpponent = 15f,
                Accuracy = 0f,
                AccuracyProficiency = 0f
            };

            // Enemy enters full cover (completely concealed)
            enemy.EnterCover(CoverState.Full, 0);
            Assert.Equal(CoverState.Full, enemy.CurrentCover);

            var combat = new CombatSystemV2(player, enemy, seed: 300 + i);

            var playerIntents = new SimultaneousIntents(player.Id)
            {
                Primary = PrimaryAction.Fire,
                Movement = MovementAction.Stand
            };

            // Enemy in full cover cannot shoot
            var enemyIntents = new SimultaneousIntents(enemy.Id)
            {
                Movement = MovementAction.Stand
            };

            combat.SubmitIntents(player, playerIntents);
            combat.SubmitIntents(enemy, enemyIntents);
            combat.BeginExecution();
            combat.ExecuteUntilReactionWindow();

            if (enemy.Health < enemy.MaxHealth)
                hitsLanded++;
        }

        _output.WriteLine($"Target in full cover (concealed): {hitsLanded}/{totalRounds} hits landed");
        
        // With full cover (complete concealment), NO hits should land
        Assert.Equal(0, hitsLanded);
    }
}
