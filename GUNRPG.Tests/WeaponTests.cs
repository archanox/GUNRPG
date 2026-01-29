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
    public void GetDamageAtDistance_NoDamageWithinMinRange()
    {
        var weapon = new Weapon("Test")
        {
            BaseDamage = 30f,
            MinDamageRange = 20f,
            MaxDamageRange = 40f,
            MinDamageMultiplier = 0.7f
        };
        
        float damage = weapon.GetDamageAtDistance(10f);
        
        Assert.Equal(30f, damage);
    }

    [Fact]
    public void GetDamageAtDistance_AppliesFalloff()
    {
        var weapon = new Weapon("Test")
        {
            BaseDamage = 30f,
            MinDamageRange = 20f,
            MaxDamageRange = 40f,
            MinDamageMultiplier = 0.7f
        };
        
        float damage = weapon.GetDamageAtDistance(30f); // Midpoint of falloff
        
        Assert.True(damage < 30f);
        Assert.True(damage > 21f); // 30 * 0.7 = 21
    }

    [Fact]
    public void GetDamageAtDistance_AppliesHeadshotMultiplier()
    {
        var weapon = new Weapon("Test")
        {
            BaseDamage = 30f,
            HeadshotMultiplier = 1.5f,
            MinDamageRange = 20f,
            MaxDamageRange = 40f
        };
        
        float damage = weapon.GetDamageAtDistance(10f, isHeadshot: true);
        
        Assert.Equal(45f, damage);
    }

    [Fact]
    public void WeaponFactory_CreatesM4A1()
    {
        var m4 = WeaponFactory.CreateM4A1();
        
        Assert.Equal("M4A1", m4.Name);
        Assert.Equal(833, m4.RoundsPerMinute);
        Assert.Equal(30, m4.MagazineSize);
        Assert.True(m4.BaseDamage > 0);
    }

    [Fact]
    public void WeaponFactory_CreatesAK47()
    {
        var ak = WeaponFactory.CreateAK47();
        
        Assert.Equal("AK-47", ak.Name);
        Assert.Equal(600, ak.RoundsPerMinute);
        Assert.True(ak.BaseDamage > WeaponFactory.CreateM4A1().BaseDamage); // AK should hit harder
    }

    [Fact]
    public void WeaponFactory_CreatesSokolS45()
    {
        var sokol = WeaponFactory.CreateSokolS45();
        
        Assert.Equal("SOKOL S45", sokol.Name);
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
    public void WeaponFactory_CreatesIridesOneM15Mod0()
    {
        var irides = WeaponFactory.CreateIridesOneM15Mod0();
        
        Assert.Equal("IRIDES ONE M15 MOD 0", irides.Name);
        Assert.Equal(769, irides.RoundsPerMinute);
        Assert.Equal(30, irides.MagazineSize);
        Assert.Equal(3000, irides.ReloadTimeMs);
        Assert.True(irides.BaseDamage > 0);
    }
}
