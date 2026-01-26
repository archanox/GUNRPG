using GUNRPG.Core.Time;
using Xunit;

namespace GUNRPG.Tests;

public class SimulationTimeTests
{
    [Fact]
    public void CurrentTime_StartsAtZero()
    {
        var time = new SimulationTime();
        Assert.Equal(0, time.CurrentTimeMs);
    }

    [Fact]
    public void Advance_IncreasesTime()
    {
        var time = new SimulationTime();
        time.Advance(100);
        Assert.Equal(100, time.CurrentTimeMs);
        
        time.Advance(50);
        Assert.Equal(150, time.CurrentTimeMs);
    }

    [Fact]
    public void Advance_ThrowsOnNegativeDelta()
    {
        var time = new SimulationTime();
        Assert.Throws<ArgumentException>(() => time.Advance(-10));
    }

    [Fact]
    public void Reset_ResetsToZero()
    {
        var time = new SimulationTime();
        time.Advance(500);
        time.Reset();
        Assert.Equal(0, time.CurrentTimeMs);
    }
}
