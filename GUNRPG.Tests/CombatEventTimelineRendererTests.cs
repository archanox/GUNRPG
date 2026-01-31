using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Rendering;
using Xunit;

namespace GUNRPG.Tests;

public class CombatEventTimelineRendererTests
{
    [Fact]
    public void BuildTimelineEntries_SortsByTimeThenActor()
    {
        var player = new Operator("Player");
        var enemy = new Operator("Enemy");
        var renderer = new CombatEventTimelineRenderer();

        var events = new List<ISimulationEvent>
        {
            new MicroReactionEvent(10, player.Id, 0, actionDurationMs: 0),
            new MicroReactionEvent(10, enemy.Id, 1, actionDurationMs: 0),
            new MicroReactionEvent(5, player.Id, 2, actionDurationMs: 0)
        };

        var entries = renderer.BuildTimelineEntries(events, player, enemy);

        Assert.Equal(3, entries.Count);
        Assert.Equal(5, entries[0].StartTimeMs);
        Assert.Equal("MicroReaction", entries[0].EventType);
        Assert.Equal("Enemy", entries[1].ActorName);
        Assert.Equal("Player", entries[2].ActorName);
    }

    [Fact]
    public void BuildTimelineEntries_ClampsNegativeStartsAndUsesDurations()
    {
        var player = new Operator("Player");
        var enemy = new Operator("Enemy");
        var renderer = new CombatEventTimelineRenderer();

        var events = new List<ISimulationEvent>
        {
            new ReloadCompleteEvent(100, player, 0, actionDurationMs: 200),
            new MovementIntervalEvent(200, enemy, distance: 1f, sequenceNumber: 1, intervalDurationMs: 150)
        };

        var entries = renderer.BuildTimelineEntries(events, player, enemy);

        Assert.Equal(0, entries[0].StartTimeMs);
        Assert.Equal(100, entries[0].EndTimeMs);
        Assert.Equal(50, entries[1].StartTimeMs);
        Assert.Equal(200, entries[1].EndTimeMs);
    }
}
