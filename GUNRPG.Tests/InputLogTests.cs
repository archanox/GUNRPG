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
                new InputLogEntry(3, first),
                new InputLogEntry(1, second),
                new InputLogEntry(2, third)
            ]);

        Assert.Collection(
            log.Entries,
            entry => Assert.Equal(second, entry.Action),
            entry => Assert.Equal(third, entry.Action),
            entry => Assert.Equal(first, entry.Action));
    }

    [Fact]
    public void Constructor_RejectsDuplicateTickEntries()
    {
        var ex = Assert.Throws<ArgumentException>(() => new InputLog(
            Guid.NewGuid(),
            Guid.NewGuid(),
            11,
            [
                new InputLogEntry(0, new MoveAction(Direction.North)),
                new InputLogEntry(0, new ExfilAction())
            ]));

        Assert.Contains("Duplicate input at tick 0", ex.Message);
    }

    [Fact]
    public void Frames_ArePopulatedWithPlayerId()
    {
        var playerId = Guid.NewGuid();
        var log = new InputLog(
            Guid.NewGuid(),
            playerId,
            42,
            [new InputLogEntry(0, new ExfilAction())]);

        Assert.Single(log.Frames);
        Assert.Equal(playerId, log.Frames[0].PlayerId);
        Assert.Equal(0, log.Frames[0].Tick);
        Assert.IsType<ExfilAction>(log.Frames[0].Intent);
    }
}
