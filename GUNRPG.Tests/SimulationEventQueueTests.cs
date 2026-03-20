using GUNRPG.Core.Simulation;
using Xunit;

namespace GUNRPG.Tests;

public sealed class SimulationEventQueueTests
{
    [Fact]
    public void Queue_OrdersEntriesByTickThenSequence()
    {
        var queue = new EventQueue<string>();

        queue.Schedule(2, 1, "tick2-seq1");
        queue.Schedule(1, 2, "tick1-seq2");
        queue.Schedule(1, 1, "tick1-seq1");

        Assert.Equal("tick1-seq1", queue.DequeueNext()!.Value);
        Assert.Equal("tick1-seq2", queue.DequeueNext()!.Value);
        Assert.Equal("tick2-seq1", queue.DequeueNext()!.Value);
    }

    [Fact]
    public void Queue_ThrowsWhenTickAndSequenceCollide()
    {
        var queue = new EventQueue<string>();

        queue.Schedule(4, 2, "first");

        var ex = Assert.Throws<InvalidOperationException>(() => queue.Schedule(4, 2, "duplicate"));
        Assert.Contains("tick 4", ex.Message);
        Assert.Contains("sequence 2", ex.Message);
    }
}
