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
}
