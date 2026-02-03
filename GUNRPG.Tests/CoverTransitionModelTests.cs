using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Unit tests for the CoverTransitionModel - cover transition delays and exposure windows.
/// </summary>
public class CoverTransitionModelTests
{
    #region Transition Delay Tests

    [Fact]
    public void GetTransitionDelayMs_SameState_ReturnsZero()
    {
        Assert.Equal(0, CoverTransitionModel.GetTransitionDelayMs(CoverState.None, CoverState.None));
        Assert.Equal(0, CoverTransitionModel.GetTransitionDelayMs(CoverState.Partial, CoverState.Partial));
        Assert.Equal(0, CoverTransitionModel.GetTransitionDelayMs(CoverState.Full, CoverState.Full));
    }

    [Fact]
    public void GetTransitionDelayMs_NoneToPartial_ReturnsConfiguredDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.None, CoverState.Partial);
        Assert.Equal(CoverTransitionModel.NoneToPartialDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_PartialToFull_ReturnsConfiguredDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.Partial, CoverState.Full);
        Assert.Equal(CoverTransitionModel.PartialToFullDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_FullToPartial_ReturnsConfiguredDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.Full, CoverState.Partial);
        Assert.Equal(CoverTransitionModel.FullToPartialDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_PartialToNone_ReturnsConfiguredDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.Partial, CoverState.None);
        Assert.Equal(CoverTransitionModel.PartialToNoneDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_NoneToFull_ReturnsCombinedDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.None, CoverState.Full);
        Assert.Equal(CoverTransitionModel.NoneToFullDelayMs, delay);
        Assert.Equal(CoverTransitionModel.NoneToPartialDelayMs + CoverTransitionModel.PartialToFullDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_FullToNone_ReturnsCombinedDelay()
    {
        int delay = CoverTransitionModel.GetTransitionDelayMs(CoverState.Full, CoverState.None);
        Assert.Equal(CoverTransitionModel.FullToNoneDelayMs, delay);
        Assert.Equal(CoverTransitionModel.FullToPartialDelayMs + CoverTransitionModel.PartialToNoneDelayMs, delay);
    }

    [Fact]
    public void GetTransitionDelayMs_ExitingFullCoverTakesLonger()
    {
        int fullToPartial = CoverTransitionModel.GetTransitionDelayMs(CoverState.Full, CoverState.Partial);
        int partialToFull = CoverTransitionModel.GetTransitionDelayMs(CoverState.Partial, CoverState.Full);

        Assert.True(fullToPartial >= partialToFull,
            $"Exiting full cover ({fullToPartial}ms) should take at least as long as entering ({partialToFull}ms)");
    }

    #endregion

    #region Delay Range Validation

    [Fact]
    public void TransitionDelays_AreWithinRealisticRange()
    {
        // All delays should be between 80ms and 250ms as per design
        Assert.InRange(CoverTransitionModel.NoneToPartialDelayMs, 80, 250);
        Assert.InRange(CoverTransitionModel.PartialToFullDelayMs, 80, 250);
        Assert.InRange(CoverTransitionModel.FullToPartialDelayMs, 80, 250);
        Assert.InRange(CoverTransitionModel.PartialToNoneDelayMs, 80, 250);
    }

    [Fact]
    public void MultiStepTransitionDelays_AreSumOfComponents()
    {
        Assert.Equal(
            CoverTransitionModel.NoneToPartialDelayMs + CoverTransitionModel.PartialToFullDelayMs,
            CoverTransitionModel.NoneToFullDelayMs);

        Assert.Equal(
            CoverTransitionModel.FullToPartialDelayMs + CoverTransitionModel.PartialToNoneDelayMs,
            CoverTransitionModel.FullToNoneDelayMs);
    }

    [Fact]
    public void CombinedTransitionDelays_AreWithinReasonableRange()
    {
        // Combined delays (multi-step transitions) should be reasonable
        // NoneToFull and FullToNone go through intermediate state, so they're longer
        // Expected range: 160ms to 500ms for combined transitions
        Assert.InRange(CoverTransitionModel.NoneToFullDelayMs, 160, 500);
        Assert.InRange(CoverTransitionModel.FullToNoneDelayMs, 160, 500);
    }

    #endregion
}
