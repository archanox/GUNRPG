using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

public class MovementModelTests
{
    [Theory]
    [InlineData(MovementState.Stationary, 1.0f)]
    [InlineData(MovementState.Idle, 1.0f)]
    [InlineData(MovementState.Walking, 0.85f)]
    [InlineData(MovementState.Sprinting, 0.45f)]
    [InlineData(MovementState.Crouching, 1.1f)]
    [InlineData(MovementState.Sliding, 0.45f)]
    public void GetAccuracyMultiplier_ReturnsCorrectValues(MovementState state, float expected)
    {
        float actual = MovementModel.GetAccuracyMultiplier(state);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(MovementState.Stationary, 0.0f)]
    [InlineData(MovementState.Idle, 0.0f)]
    [InlineData(MovementState.Walking, 0.05f)]
    [InlineData(MovementState.Sprinting, 0.15f)]
    [InlineData(MovementState.Crouching, 0.02f)]
    [InlineData(MovementState.Sliding, 0.15f)]
    public void GetWeaponSwayDegrees_ReturnsCorrectValues(MovementState state, float expected)
    {
        float actual = MovementModel.GetWeaponSwayDegrees(state);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(MovementState.Stationary, 1.0f)]
    [InlineData(MovementState.Idle, 1.0f)]
    [InlineData(MovementState.Walking, 1.2f)]
    [InlineData(MovementState.Sprinting, 1.6f)]
    [InlineData(MovementState.Crouching, 0.9f)]
    [InlineData(MovementState.Sliding, 1.6f)]
    public void GetADSTimeMultiplier_ReturnsCorrectValues(MovementState state, float expected)
    {
        float actual = MovementModel.GetADSTimeMultiplier(state);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(MovementState.Stationary, 1.0f)]
    [InlineData(MovementState.Idle, 1.0f)]
    [InlineData(MovementState.Walking, 1.15f)]
    [InlineData(MovementState.Sprinting, 1.3f)]
    [InlineData(MovementState.Crouching, 0.8f)]
    [InlineData(MovementState.Sliding, 1.3f)]
    public void GetSuppressionBuildupMultiplier_ReturnsCorrectValues(MovementState state, float expected)
    {
        float actual = MovementModel.GetSuppressionBuildupMultiplier(state);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Fact]
    public void GetSuppressionDecayMultiplier_Crouching_ReturnsFasterDecay()
    {
        float crouchDecay = MovementModel.GetSuppressionDecayMultiplier(MovementState.Crouching);
        float normalDecay = MovementModel.GetSuppressionDecayMultiplier(MovementState.Stationary);
        
        Assert.True(crouchDecay > normalDecay);
        Assert.Equal(1.4f, crouchDecay, precision: 2);
    }

    [Theory]
    [InlineData(CoverState.None, false, 1.0f)]
    [InlineData(CoverState.Partial, false, 0.7f)]
    [InlineData(CoverState.Full, false, 0.0f)]
    [InlineData(CoverState.Full, true, 1.0f)]
    public void GetCoverHitProbabilityMultiplier_ReturnsCorrectValues(CoverState cover, bool isPeeking, float expected)
    {
        float actual = MovementModel.GetCoverHitProbabilityMultiplier(cover, isPeeking);
        Assert.Equal(expected, actual, precision: 2);
    }

    [Theory]
    [InlineData(MovementState.Stationary, true)]
    [InlineData(MovementState.Idle, true)]
    [InlineData(MovementState.Crouching, true)]
    [InlineData(MovementState.Walking, false)]
    [InlineData(MovementState.Sprinting, false)]
    [InlineData(MovementState.Sliding, false)]
    public void CanEnterCover_ReturnsCorrectValues(MovementState state, bool expected)
    {
        bool actual = MovementModel.CanEnterCover(state);
        Assert.Equal(expected, actual);
    }
}
