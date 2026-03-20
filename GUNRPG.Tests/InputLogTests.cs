using GUNRPG.Core.Simulation;
using Xunit;

namespace GUNRPG.Tests;

public sealed class InputLogTests
{
    [Fact]
    public void FromRunInput_ThrowsWithActionIndex_WhenActionIsNull()
    {
        var input = new RunInput
        {
            RunId = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            Seed = 7,
            Actions =
            [
                new ExfilAction(),
                null!
            ]
        };

        var ex = Assert.Throws<ArgumentException>(() => InputLog.FromRunInput(input));
        Assert.Contains("index 1", ex.Message);
    }

    [Fact]
    public void Constructor_NormalizesEntriesIntoTickOrder()
    {
        var first = new MoveAction(Direction.North);
        var second = new ExfilAction();
        var third = new UseItemAction(Guid.Parse("44444444-4444-4444-4444-444444444444"));

        var log = new InputLog(
            Guid.NewGuid(),
            Guid.NewGuid(),
            11,
            [
                new InputLogEntry(2, first),
                new InputLogEntry(1, second),
                new InputLogEntry(2, third)
            ]);

        Assert.Collection(
            log.Entries,
            entry => Assert.Equal(second, entry.Action),
            entry => Assert.Equal(first, entry.Action),
            entry => Assert.Equal(third, entry.Action));
    }
}
