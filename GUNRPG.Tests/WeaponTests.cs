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
}
