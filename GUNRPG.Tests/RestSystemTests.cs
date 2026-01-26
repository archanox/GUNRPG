using GUNRPG.Core.Operators;
using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class RestSystemTests
{
    [Fact]
    public void IsReadyForCombat_TrueWhenFatigueUnderThreshold()
    {
        var restSystem = new RestSystem();
        var op = new Operator("Test")
        {
            Fatigue = 50f
        };

        Assert.True(restSystem.IsReadyForCombat(op));
    }

    [Fact]
    public void IsReadyForCombat_FalseWhenFatigueOverThreshold()
    {
        var restSystem = new RestSystem();
        var op = new Operator("Test")
        {
            Fatigue = 90f
        };

        Assert.False(restSystem.IsReadyForCombat(op));
    }

    [Fact]
    public void ApplyPostCombatFatigue_IncreasesFatigue()
    {
        var restSystem = new RestSystem
        {
            FatiguePerCombat = 20f
        };
        var op = new Operator("Test")
        {
            Fatigue = 30f
        };

        restSystem.ApplyPostCombatFatigue(op);

        Assert.Equal(50f, op.Fatigue);
    }

    [Fact]
    public void Rest_ReducesFatigue()
    {
        var restSystem = new RestSystem
        {
            FatigueRecoveryPerHour = 30f
        };
        var op = new Operator("Test")
        {
            Fatigue = 60f
        };

        restSystem.Rest(op, 1.0f); // 1 hour rest

        Assert.Equal(30f, op.Fatigue);
    }

    [Fact]
    public void Rest_RestoresHealth()
    {
        var restSystem = new RestSystem();
        var op = new Operator("Test")
        {
            Health = 50f
        };

        restSystem.Rest(op, 1.0f);

        Assert.Equal(op.MaxHealth, op.Health);
    }

    [Fact]
    public void GetMinimumRestHours_CalculatesCorrectly()
    {
        var restSystem = new RestSystem
        {
            FatigueRecoveryPerHour = 30f,
            MaxDeployableFatigue = 80f
        };
        var op = new Operator("Test")
        {
            Fatigue = 95f
        };

        float minRest = restSystem.GetMinimumRestHours(op);

        // 95 - 80 = 15 fatigue to recover
        // 15 / 30 = 0.5 hours
        Assert.Equal(0.5f, minRest);
    }

    [Fact]
    public void OperatorManager_PrepareForCombat_RestoresState()
    {
        var manager = new OperatorManager();
        var op = new Operator("Test")
        {
            Health = 50f,
            Stamina = 30f,
            Fatigue = 20f
        };
        op.EquippedWeapon = new GUNRPG.Core.Weapons.Weapon("Test")
        {
            MagazineSize = 30
        };

        bool prepared = manager.PrepareForCombat(op);

        Assert.True(prepared);
        Assert.Equal(op.MaxHealth, op.Health);
        Assert.Equal(op.MaxStamina, op.Stamina);
        Assert.Equal(30, op.CurrentAmmo);
    }

    [Fact]
    public void OperatorManager_PrepareForCombat_FailsWhenTooFatigued()
    {
        var manager = new OperatorManager();
        var op = new Operator("Test")
        {
            Fatigue = 90f
        };

        bool prepared = manager.PrepareForCombat(op);

        Assert.False(prepared);
    }

    [Fact]
    public void OperatorManager_RestTracking()
    {
        var manager = new OperatorManager();
        var op = new Operator("Test");

        Assert.False(manager.IsResting(op));

        manager.SendToRest(op);

        Assert.True(manager.IsResting(op));
        Assert.True(manager.GetRestDuration(op) >= TimeSpan.Zero);
    }

    [Fact]
    public void OperatorManager_WakeFromRest()
    {
        var manager = new OperatorManager();
        var op = new Operator("Test")
        {
            Fatigue = 50f,
            Health = 50f
        };

        manager.SendToRest(op);
        System.Threading.Thread.Sleep(10); // Small delay
        manager.WakeFromRest(op);

        Assert.False(manager.IsResting(op));
        Assert.Equal(op.MaxHealth, op.Health); // Health restored
    }
}
