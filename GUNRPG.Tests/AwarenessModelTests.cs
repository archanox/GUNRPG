using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Unit tests for the AwarenessModel - visibility and recognition mechanics.
/// </summary>
public class AwarenessModelTests
{
    #region CanSeeTarget Tests

    [Fact]
    public void CanSeeTarget_NoCover_ReturnsTrue()
    {
        Assert.True(AwarenessModel.CanSeeTarget(CoverState.None));
    }

    [Fact]
    public void CanSeeTarget_PartialCover_ReturnsTrue()
    {
        Assert.True(AwarenessModel.CanSeeTarget(CoverState.Partial));
    }

    [Fact]
    public void CanSeeTarget_FullCover_ReturnsFalse()
    {
        Assert.False(AwarenessModel.CanSeeTarget(CoverState.Full));
    }

    #endregion

    #region GetVisibilityLevel Tests

    [Fact]
    public void GetVisibilityLevel_NoCover_ReturnsOne()
    {
        Assert.Equal(1.0f, AwarenessModel.GetVisibilityLevel(CoverState.None));
    }

    [Fact]
    public void GetVisibilityLevel_PartialCover_ReturnsHalf()
    {
        Assert.Equal(0.5f, AwarenessModel.GetVisibilityLevel(CoverState.Partial));
    }

    [Fact]
    public void GetVisibilityLevel_FullCover_ReturnsZero()
    {
        Assert.Equal(0.0f, AwarenessModel.GetVisibilityLevel(CoverState.Full));
    }

    #endregion

    #region Recognition Delay Tests

    [Fact]
    public void CalculateRecognitionDelayMs_HighProficiency_ReturnsLowDelay()
    {
        float delay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 1.0f,
            observerSuppressionLevel: 0f);

        // High proficiency should approach minimum delay
        Assert.True(delay <= AwarenessModel.MinRecognitionDelayMs + 10f,
            $"High proficiency should result in low delay. Got: {delay}ms");
    }

    [Fact]
    public void CalculateRecognitionDelayMs_LowProficiency_ReturnsHighDelay()
    {
        float delay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.0f,
            observerSuppressionLevel: 0f);

        // Low proficiency should result in base delay
        Assert.True(delay >= AwarenessModel.BaseRecognitionDelayMs * 0.9f,
            $"Low proficiency should result in high delay. Got: {delay}ms");
    }

    [Fact]
    public void CalculateRecognitionDelayMs_SuppressionIncreasesDelay()
    {
        float noSuppressionDelay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.5f,
            observerSuppressionLevel: 0f);

        float fullSuppressionDelay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.5f,
            observerSuppressionLevel: 1.0f);

        Assert.True(fullSuppressionDelay > noSuppressionDelay,
            $"Suppression should increase delay. No suppression: {noSuppressionDelay}ms, Full suppression: {fullSuppressionDelay}ms");
    }

    [Fact]
    public void CalculateRecognitionDelayMs_ClampsToMinimum()
    {
        float delay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 1.0f,
            observerSuppressionLevel: 0f);

        Assert.True(delay >= AwarenessModel.MinRecognitionDelayMs,
            $"Delay should be at least {AwarenessModel.MinRecognitionDelayMs}ms. Got: {delay}ms");
    }

    [Fact]
    public void CalculateRecognitionDelayMs_ClampsToMaximum()
    {
        float delay = AwarenessModel.CalculateRecognitionDelayMs(
            observerAccuracyProficiency: 0.0f,
            observerSuppressionLevel: 1.0f);

        Assert.True(delay <= AwarenessModel.MaxRecognitionDelayMs,
            $"Delay should be at most {AwarenessModel.MaxRecognitionDelayMs}ms. Got: {delay}ms");
    }

    #endregion

    #region Recognition Accuracy Multiplier Tests

    [Fact]
    public void GetRecognitionAccuracyMultiplier_ZeroProgress_ReturnsLowMultiplier()
    {
        float multiplier = AwarenessModel.GetRecognitionAccuracyMultiplier(0f);

        Assert.Equal(AwarenessModel.RecognitionPenaltyAccuracyMultiplier, multiplier);
    }

    [Fact]
    public void GetRecognitionAccuracyMultiplier_FullProgress_ReturnsOne()
    {
        float multiplier = AwarenessModel.GetRecognitionAccuracyMultiplier(1.0f);

        Assert.Equal(1.0f, multiplier);
    }

    [Fact]
    public void GetRecognitionAccuracyMultiplier_HalfProgress_ReturnsMidValue()
    {
        float multiplier = AwarenessModel.GetRecognitionAccuracyMultiplier(0.5f);

        float expected = AwarenessModel.RecognitionPenaltyAccuracyMultiplier +
                        (1.0f - AwarenessModel.RecognitionPenaltyAccuracyMultiplier) * 0.5f;

        Assert.Equal(expected, multiplier, precision: 3);
    }

    #endregion
}
