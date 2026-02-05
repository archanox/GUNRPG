using GUNRPG.Core;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Tests.Core;

/// <summary>
/// Domain-level tests for simultaneous intent validation.
/// Tests the coordination and validation of multiple concurrent intents.
/// </summary>
public class SimultaneousIntentsValidationTests
{
    [Fact]
    public void SimultaneousIntents_ValidatesPrimaryAction()
    {
        var op = CreateOperator();
        op.CurrentAmmo = 0;

        var intents = new SimultaneousIntents(op.Id)
        {
            Primary = PrimaryAction.Fire
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.False(isValid);
        Assert.Contains("ammo", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SimultaneousIntents_ValidatesMovementAction()
    {
        var op = CreateOperator();
        op.Stamina = 0;

        var intents = new SimultaneousIntents(op.Id)
        {
            Movement = MovementAction.SprintToward
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.False(isValid);
        Assert.Contains("stamina", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SimultaneousIntents_ValidatesStanceAction()
    {
        var op = CreateOperator();
        op.AimState = AimState.ADS;

        var intents = new SimultaneousIntents(op.Id)
        {
            Stance = StanceAction.EnterADS
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.False(isValid);
        Assert.Contains("already", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SimultaneousIntents_RejectsReloadWhileSliding()
    {
        var op = CreateOperator();
        op.CurrentAmmo = 5;

        var intents = new SimultaneousIntents(op.Id)
        {
            Primary = PrimaryAction.Reload,
            Movement = MovementAction.SlideToward
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.False(isValid);
        Assert.Contains("sliding", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SimultaneousIntents_AllowsFireAndMovement()
    {
        var op = CreateOperator();

        var intents = new SimultaneousIntents(op.Id)
        {
            Primary = PrimaryAction.Fire,
            Movement = MovementAction.WalkToward
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void SimultaneousIntents_AllowsFireAndStance()
    {
        var op = CreateOperator();

        var intents = new SimultaneousIntents(op.Id)
        {
            Primary = PrimaryAction.Fire,
            Stance = StanceAction.EnterADS
        };

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void SimultaneousIntents_CreateStop_ReturnsValidIntent()
    {
        var op = CreateOperator();
        var intents = SimultaneousIntents.CreateStop(op.Id);

        var (isValid, errorMessage) = intents.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
        Assert.False(intents.HasAnyAction());
    }

    private static Operator CreateOperator()
    {
        return new Operator("TestOperator")
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            CurrentAmmo = 30,
            Stamina = 100,
            AimState = AimState.Hip
        };
    }
}
