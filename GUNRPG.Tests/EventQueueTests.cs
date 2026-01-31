using GUNRPG.Core.Events;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class EventQueueTests
{
    private class TestEventWithOperator : ISimulationEvent
    {
        public long EventTimeMs { get; }
        public Guid OperatorId { get; }
        public int SequenceNumber { get; }

        public TestEventWithOperator(long eventTimeMs, Guid operatorId, int sequenceNumber)
        {
            EventTimeMs = eventTimeMs;
            OperatorId = operatorId;
            SequenceNumber = sequenceNumber;
        }

        public bool Execute()
        {
            return false;
        }
    }
    private class TestEvent : ISimulationEvent
    {
        public long EventTimeMs { get; }
        public Guid OperatorId { get; }
        public int SequenceNumber { get; }
        public bool Executed { get; private set; }

        public TestEvent(long timeMs, Guid operatorId, int sequence)
        {
            EventTimeMs = timeMs;
            OperatorId = operatorId;
            SequenceNumber = sequence;
        }

        public bool Execute()
        {
            Executed = true;
            return false;
        }
    }


    [Fact]
    public void EventQueue_StartsEmpty()
    {
        var queue = new EventQueue();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Schedule_AddsEvent()
    {
        var queue = new EventQueue();
        var evt = new TestEvent(100, Guid.NewGuid(), 0);
        
        queue.Schedule(evt);
        
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void DequeueNext_ReturnsEventsInOrder()
    {
        var queue = new EventQueue();
        var operatorId = Guid.NewGuid();
        
        var evt1 = new TestEvent(100, operatorId, 0);
        var evt2 = new TestEvent(50, operatorId, 1);
        var evt3 = new TestEvent(200, operatorId, 2);
        
        queue.Schedule(evt1);
        queue.Schedule(evt2);
        queue.Schedule(evt3);
        
        var first = queue.DequeueNext();
        Assert.Equal(50, first!.EventTimeMs);
        
        var second = queue.DequeueNext();
        Assert.Equal(100, second!.EventTimeMs);
        
        var third = queue.DequeueNext();
        Assert.Equal(200, third!.EventTimeMs);
    }

    [Fact]
    public void RemoveEventsForOperator_RemovesAllOperatorEvents()
    {
        var queue = new EventQueue();
        var op1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var op2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        
        queue.Schedule(new TestEvent(100, op1, 0));
        queue.Schedule(new TestEvent(150, op2, 1));
        queue.Schedule(new TestEvent(200, op1, 2));
        
        Assert.Equal(3, queue.Count);
        
        queue.RemoveEventsForOperator(op1);
        
        Assert.Equal(1, queue.Count);
        var remaining = queue.DequeueNext();
        Assert.Equal(op2, remaining!.OperatorId);
    }

    [Fact]
    public void Clear_RemovesAllEvents()
    {
        var queue = new EventQueue();
        queue.Schedule(new TestEvent(100, Guid.NewGuid(), 0));
        queue.Schedule(new TestEvent(200, Guid.NewGuid(), 1));
        
        queue.Clear();
        
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void EventQueue_OrdersEventsByTimeThenOperator()
    {
        var queue = new EventQueue();
        var op1 = Guid.NewGuid();
        var op2 = Guid.NewGuid();

        queue.Schedule(new TestEventWithOperator(50, op2, 1));
        queue.Schedule(new TestEventWithOperator(50, op1, 0));
        queue.Schedule(new TestEventWithOperator(25, op1, 2));

        var first = queue.DequeueNext();
        var second = queue.DequeueNext();
        var third = queue.DequeueNext();

        Assert.Equal(25, first!.EventTimeMs);
        Assert.Equal(op1, first.OperatorId);
        Assert.Equal(50, second!.EventTimeMs);
        Assert.Equal(op1, second.OperatorId);
        Assert.Equal(50, third!.EventTimeMs);
        Assert.Equal(op2, third.OperatorId);
    }

}
