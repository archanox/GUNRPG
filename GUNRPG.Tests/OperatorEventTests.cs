using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorEventTests
{
    [Fact]
    public void OperatorCreatedEvent_ShouldHaveSequenceZero()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var name = "TestOperator";

        // Act
        var evt = new OperatorCreatedEvent(operatorId, name);

        // Assert
        Assert.Equal(0, evt.SequenceNumber);
        Assert.Equal(string.Empty, evt.PreviousHash);
        Assert.NotEmpty(evt.Hash);
    }

    [Fact]
    public void OperatorEvent_ShouldVerifyHashCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "Test");

        // Act
        var isValid = evt.VerifyHash();

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void OperatorEvent_ShouldComputeDeterministicHash()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var name = "TestOperator";

        // Act - Create two events with same data
        var evt1 = new OperatorCreatedEvent(operatorId, name);
        var evt2 = new OperatorCreatedEvent(operatorId, name);

        // Assert - Hashes should be identical (deterministic)
        Assert.Equal(evt1.Hash, evt2.Hash);
    }

    [Fact]
    public void OperatorEvent_VerifyChain_ShouldAcceptFirstEvent()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new OperatorCreatedEvent(operatorId, "Test");

        // Act
        var isValid = evt.VerifyChain(null);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void OperatorEvent_VerifyChain_ShouldAcceptValidSequence()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "Test");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);

        // Act
        var isValid = evt2.VerifyChain(evt1);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void OperatorEvent_VerifyChain_ShouldRejectGapInSequence()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "Test");
        var evt2 = new XpGainedEvent(operatorId, 2, 100, "Victory", evt1.Hash); // Gap: should be sequence 1

        // Act
        var isValid = evt2.VerifyChain(evt1);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void OperatorEvent_VerifyChain_ShouldRejectMismatchedHash()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "Test");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", "wrong_hash");

        // Act
        var isValid = evt2.VerifyChain(evt1);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void XpGainedEvent_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new XpGainedEvent(operatorId, 1, 150, "Victory", string.Empty);

        // Act
        var (xpAmount, reason) = evt.GetPayload();

        // Assert
        Assert.Equal(150, xpAmount);
        Assert.Equal("Victory", reason);
    }

    [Fact]
    public void WoundsTreatedEvent_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new WoundsTreatedEvent(operatorId, 1, 50.5f, string.Empty);

        // Act
        var healthRestored = evt.GetHealthRestored();

        // Assert
        Assert.Equal(50.5f, healthRestored);
    }

    [Fact]
    public void LoadoutChangedEvent_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new LoadoutChangedEvent(operatorId, 1, "AK-47", string.Empty);

        // Act
        var weaponName = evt.GetWeaponName();

        // Assert
        Assert.Equal("AK-47", weaponName);
    }

    [Fact]
    public void PerkUnlockedEvent_ShouldSerializePayloadCorrectly()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt = new PerkUnlockedEvent(operatorId, 1, "Fast Reload", string.Empty);

        // Act
        var perkName = evt.GetPerkName();

        // Assert
        Assert.Equal("Fast Reload", perkName);
    }

    [Fact]
    public void OperatorEvent_HashChain_ShouldPreventTampering()
    {
        // Arrange
        var operatorId = OperatorId.NewId();
        var evt1 = new OperatorCreatedEvent(operatorId, "Test");
        var evt2 = new XpGainedEvent(operatorId, 1, 100, "Victory", evt1.Hash);
        var evt3 = new XpGainedEvent(operatorId, 2, 50, "Survived", evt2.Hash);

        // Act - Try to insert a tampered event between evt2 and evt3
        var tamperedEvt = new WoundsTreatedEvent(operatorId, 2, 25, evt2.Hash);

        // Assert - evt3 should not verify against tamperedEvt
        Assert.False(evt3.VerifyChain(tamperedEvt));
    }
}
