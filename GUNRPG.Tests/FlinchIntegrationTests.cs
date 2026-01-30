using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

public class FlinchIntegrationTests
{
    [Fact]
    public void DamageAppliedEvent_AppliesFlinchToTarget()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        var attacker = new Operator("Attacker")
        {
            EquippedWeapon = weapon,
            CurrentAmmo = 30
        };
        var defender = new Operator("Defender")
        {
            EquippedWeapon = weapon
        };

        float damage = 20f;
        var evt = new DamageAppliedEvent(
            eventTimeMs: 0,
            shooter: attacker,
            target: defender,
            damage: damage,
            bodyPart: BodyPart.UpperTorso,
            sequenceNumber: 0,
            weaponName: weapon.Name);

        evt.Execute();

        Assert.True(defender.FlinchSeverity > 0f);
        Assert.Equal(1, defender.FlinchShotsRemaining);
    }

    [Fact]
    public void ShotFiredEvent_ConsumesFlinchAfterShot()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        var shooter = new Operator("Shooter")
        {
            EquippedWeapon = weapon,
            CurrentAmmo = 30,
            Accuracy = 1.0f
        };
        var target = new Operator("Target")
        {
            EquippedWeapon = weapon
        };

        shooter.ApplyFlinch(0.5f);
        Assert.Equal(1, shooter.FlinchShotsRemaining);

        var evt = new ShotFiredEvent(
            eventTimeMs: 0,
            shooter: shooter,
            target: target,
            sequenceNumber: 0,
            random: new Random(1),
            eventQueue: null);

        evt.Execute();

        Assert.Equal(0, shooter.FlinchShotsRemaining);
        Assert.Equal(0f, shooter.FlinchSeverity, 3);
    }
}
