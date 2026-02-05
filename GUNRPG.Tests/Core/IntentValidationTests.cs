using GUNRPG.Core;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;

namespace GUNRPG.Tests.Core;

/// <summary>
/// Domain-level tests for intent validation logic.
/// These tests focus on the core domain rules without application or API concerns.
/// </summary>
public class IntentValidationTests
{
    [Fact]
    public void FireWeaponIntent_RequiresAmmo()
    {
        var op = CreateOperator();
        op.CurrentAmmo = 0;

        var intent = new FireWeaponIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.False(isValid);
        Assert.Contains("no ammo", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FireWeaponIntent_ValidWithAmmo()
    {
        var op = CreateOperator();
        op.CurrentAmmo = 10;

        var intent = new FireWeaponIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void ReloadIntent_FailsWhenMagazineFull()
    {
        var op = CreateOperator();
        op.CurrentAmmo = op.EquippedWeapon!.MagazineSize;

        var intent = new ReloadIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.False(isValid);
        Assert.Contains("full", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReloadIntent_ValidWhenNotFull()
    {
        var op = CreateOperator();
        op.CurrentAmmo = 5;

        var intent = new ReloadIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void SprintIntent_RequiresStamina()
    {
        var op = CreateOperator();
        op.Stamina = 0;

        var intent = new SprintIntent(op.Id, towardOpponent: true);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.False(isValid);
        Assert.Contains("stamina", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SprintIntent_ValidWithStamina()
    {
        var op = CreateOperator();
        op.Stamina = 50;

        var intent = new SprintIntent(op.Id, towardOpponent: true);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void EnterADSIntent_FailsWhenAlreadyInADS()
    {
        var op = CreateOperator();
        op.AimState = AimState.ADS;

        var intent = new EnterADSIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.False(isValid);
        Assert.Contains("already", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnterADSIntent_ValidWhenInHipFire()
    {
        var op = CreateOperator();
        op.AimState = AimState.Hip;

        var intent = new EnterADSIntent(op.Id);
        var (isValid, errorMessage) = intent.Validate(op);

        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    private static Operator CreateOperator()
    {
        return new Operator("TestOperator")
        {
            EquippedWeapon = WeaponFactory.CreateSokol545(),
            CurrentAmmo = 30,
            Stamina = 100
        };
    }
}
