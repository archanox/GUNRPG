using GUNRPG.Core;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

public class WeaponTests
{
    [Fact]
    public void GetTimeBetweenShotsMs_CalculatesCorrectly()
    {
        var weapon = new Weapon("Test")
        {
            RoundsPerMinute = 600 // 10 rounds per second
        };
        
        float timeBetween = weapon.GetTimeBetweenShotsMs();
        
        Assert.Equal(100f, timeBetween);
    }

    [Fact]
    public void WeaponFactory_CreatesSokol545()
    {
        var sokol = WeaponFactory.CreateSokol545();
        
        Assert.Equal("SOKOL 545", sokol.Name);
        Assert.Equal(583, sokol.RoundsPerMinute);
        Assert.Equal(102, sokol.MagazineSize); // LMG has large magazine
        Assert.Equal(7333, sokol.ReloadTimeMs); // LMG has slow reload
        Assert.True(sokol.BaseDamage > 0);
    }

    [Fact]
    public void WeaponFactory_CreatesSturmwolf45()
    {
        var sturmwolf = WeaponFactory.CreateSturmwolf45();
        
        Assert.Equal("STURMWOLF 45", sturmwolf.Name);
        Assert.Equal(667, sturmwolf.RoundsPerMinute);
        Assert.Equal(32, sturmwolf.MagazineSize);
        Assert.Equal(2730, sturmwolf.ReloadTimeMs);
        Assert.True(sturmwolf.BaseDamage > 0);
    }

    [Fact]
    public void WeaponFactory_CreatesM15Mod0()
    {
        var m15 = WeaponFactory.CreateM15Mod0();
        
        Assert.Equal("M15 MOD 0", m15.Name);
        Assert.Equal(769, m15.RoundsPerMinute);
        Assert.Equal(30, m15.MagazineSize);
        Assert.Equal(3000, m15.ReloadTimeMs);
        Assert.True(m15.BaseDamage > 0);
    }

    [Fact]
    public void Sokol545_DamageRanges_ApplyCorrectly()
    {
        var sokol = WeaponFactory.CreateSokol545();
        
        // Test first range (0-51m): Head 38, Body 32
        Assert.Equal(38f, sokol.GetDamageAtDistance(25f, BodyPart.Head));
        Assert.Equal(32f, sokol.GetDamageAtDistance(25f, BodyPart.UpperTorso));
        Assert.Equal(32f, sokol.GetDamageAtDistance(25f, BodyPart.LowerLeg));
        
        // Test second range (51-71m): Head 37, Body 31
        Assert.Equal(37f, sokol.GetDamageAtDistance(60f, BodyPart.Head));
        Assert.Equal(31f, sokol.GetDamageAtDistance(60f, BodyPart.Neck));
        
        // Test third range (71m+): Head 29, Body 24
        Assert.Equal(29f, sokol.GetDamageAtDistance(80f, BodyPart.Head));
        Assert.Equal(24f, sokol.GetDamageAtDistance(100f, BodyPart.UpperArm));
    }

    [Fact]
    public void Sturmwolf45_DamageRanges_ApplyCorrectly()
    {
        var sturmwolf = WeaponFactory.CreateSturmwolf45();
        
        // Test first range (0-11m): Head 37, Body 30
        Assert.Equal(37f, sturmwolf.GetDamageAtDistance(5f, BodyPart.Head));
        Assert.Equal(30f, sturmwolf.GetDamageAtDistance(5f, BodyPart.LowerTorso));
        
        // Test second range (11-18m): Head 28, Body 23
        Assert.Equal(28f, sturmwolf.GetDamageAtDistance(15f, BodyPart.Head));
        Assert.Equal(23f, sturmwolf.GetDamageAtDistance(15f, BodyPart.UpperLeg));
        
        // Test third range (18-26m): Head 23, Body 19
        Assert.Equal(23f, sturmwolf.GetDamageAtDistance(22f, BodyPart.Head));
        Assert.Equal(19f, sturmwolf.GetDamageAtDistance(22f, BodyPart.LowerArm));
        
        // Test fourth range (26m+): Head 19, Body 16
        Assert.Equal(19f, sturmwolf.GetDamageAtDistance(30f, BodyPart.Head));
        Assert.Equal(16f, sturmwolf.GetDamageAtDistance(50f, BodyPart.UpperTorso));
    }

    [Fact]
    public void M15Mod0_DamageRanges_ApplyCorrectly()
    {
        var m15 = WeaponFactory.CreateM15Mod0();
        
        // Test first range (0-30.5m): Head 27, Body 21
        Assert.Equal(27f, m15.GetDamageAtDistance(15f, BodyPart.Head));
        Assert.Equal(21f, m15.GetDamageAtDistance(15f, BodyPart.UpperTorso));
        Assert.Equal(21f, m15.GetDamageAtDistance(15f, BodyPart.LowerLeg));
        
        // Test second range (30.5-46.4m): Head 23, Body 18
        Assert.Equal(23f, m15.GetDamageAtDistance(38f, BodyPart.Head));
        Assert.Equal(18f, m15.GetDamageAtDistance(38f, BodyPart.Neck));
        Assert.Equal(18f, m15.GetDamageAtDistance(40f, BodyPart.UpperArm));
        
        // Test third range (46.4m+): Head 22, Body 17
        Assert.Equal(22f, m15.GetDamageAtDistance(50f, BodyPart.Head));
        Assert.Equal(17f, m15.GetDamageAtDistance(60f, BodyPart.LowerTorso));
        Assert.Equal(17f, m15.GetDamageAtDistance(100f, BodyPart.UpperLeg));
    }

    [Fact]
    public void AllWeapons_HeadshotDamage_HigherThanBodyShots()
    {
        var sokol = WeaponFactory.CreateSokol545();
        var sturmwolf = WeaponFactory.CreateSturmwolf45();
        var m15 = WeaponFactory.CreateM15Mod0();
        
        // Verify headshots always deal more damage than body shots at same distance
        Assert.True(sokol.GetDamageAtDistance(25f, BodyPart.Head) > sokol.GetDamageAtDistance(25f, BodyPart.UpperTorso));
        Assert.True(sturmwolf.GetDamageAtDistance(15f, BodyPart.Head) > sturmwolf.GetDamageAtDistance(15f, BodyPart.Neck));
        Assert.True(m15.GetDamageAtDistance(40f, BodyPart.Head) > m15.GetDamageAtDistance(40f, BodyPart.LowerArm));
    }
}
