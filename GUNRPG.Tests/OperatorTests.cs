using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorTests
{
    [Fact]
    public void Operator_InitializesWithDefaults()
    {
        var op = new Operator("Test");
        
        Assert.Equal("Test", op.Name);
        Assert.Equal(100f, op.MaxHealth);
        Assert.Equal(100f, op.Health);
        Assert.True(op.IsAlive);
        Assert.Equal(MovementState.Idle, op.MovementState);
        Assert.Equal(AimState.Hip, op.AimState);
        Assert.Equal(WeaponState.Ready, op.WeaponState);
    }

    [Fact]
    public void TakeDamage_ReducesHealth()
    {
        var op = new Operator("Test");
        
        op.TakeDamage(30f, 1000);
        
        Assert.Equal(70f, op.Health);
        Assert.True(op.IsAlive);
    }

    [Fact]
    public void TakeDamage_SetsLastDamageTime()
    {
        var op = new Operator("Test");
        
        op.TakeDamage(10f, 1000);
        
        Assert.Equal(1000, op.LastDamageTimeMs);
    }

    [Fact]
    public void TakeDamage_CanKill()
    {
        var op = new Operator("Test");
        
        op.TakeDamage(150f, 1000);
        
        Assert.Equal(0f, op.Health);
        Assert.False(op.IsAlive);
    }

    [Fact]
    public void CanRegenerateHealth_RequiresDelay()
    {
        var op = new Operator("Test")
        {
            HealthRegenDelayMs = 5000
        };
        
        op.TakeDamage(20f, 1000);
        
        Assert.False(op.CanRegenerateHealth(3000)); // Too soon
        Assert.True(op.CanRegenerateHealth(6001)); // After delay
    }

    [Fact]
    public void UpdateRegeneration_RegeneratesHealth()
    {
        var op = new Operator("Test")
        {
            HealthRegenDelayMs = 1000,
            HealthRegenRate = 40f // 40 HP per second
        };
        
        op.TakeDamage(40f, 1000);
        Assert.Equal(60f, op.Health);
        
        // Wait for regen delay and regenerate for 0.5 seconds
        op.UpdateRegeneration(500, 2500); // 0.5s after regen delay
        
        Assert.True(op.Health > 60f); // Should have regenerated
        Assert.True(op.Health <= 80f); // But not more than 20 HP (0.5s * 40 HP/s)
    }

    [Fact]
    public void UpdateRegeneration_RegeneratesStamina()
    {
        var op = new Operator("Test")
        {
            StaminaRegenRate = 20f, // 20 stamina per second
            Stamina = 50f
        };
        
        op.UpdateRegeneration(1000, 1000); // 1 second
        
        Assert.Equal(70f, op.Stamina);
    }

    [Fact]
    public void UpdateRegeneration_DrainsStaminaDuringSprint()
    {
        var op = new Operator("Test")
        {
            SprintStaminaDrainRate = 10f,
            Stamina = 50f,
            MovementState = MovementState.Sprinting
        };
        
        op.UpdateRegeneration(1000, 1000); // 1 second
        
        Assert.Equal(40f, op.Stamina);
    }
}
