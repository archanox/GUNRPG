using GUNRPG.Core.Simulation;
using Xunit;

namespace GUNRPG.Tests;

public sealed class SeededRandomTests
{
    [Fact]
    public void SeededRandom_ResumesFromCapturedState()
    {
        var original = new SeededRandom(12345);
        _ = original.Next(0, 100);
        _ = original.Next(0, 100);

        var resumed = new SeededRandom(new RngState(original.Seed, original.State, original.CallCount));

        Assert.Equal(original.Next(0, 100), resumed.Next(0, 100));
        Assert.Equal(original.Next(10, 20), resumed.Next(10, 20));
    }
}
