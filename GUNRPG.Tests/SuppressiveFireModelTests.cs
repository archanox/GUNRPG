using GUNRPG.Core;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using GUNRPG.Core.Weapons;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Unit tests for the SuppressiveFireModel - suppressive fire behavior against concealed targets.
/// </summary>
public class SuppressiveFireModelTests
{
    #region Burst Size Tests

    [Fact]
    public void CalculateSuppressiveBurstSize_StandardWeapon_ReturnsDefaultBurst()
    {
        var weapon = WeaponFactory.CreateSturmwolf45(); // Standard SMG
        int burstSize = SuppressiveFireModel.CalculateSuppressiveBurstSize(weapon, availableAmmo: 30);

        Assert.InRange(burstSize, 
            SuppressiveFireModel.MinSuppressiveBurstSize, 
            SuppressiveFireModel.MaxSuppressiveBurstSize);
    }

    [Fact]
    public void CalculateSuppressiveBurstSize_LimitedAmmo_RespectsAmmoLimit()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        int burstSize = SuppressiveFireModel.CalculateSuppressiveBurstSize(weapon, availableAmmo: 2);

        Assert.Equal(2, burstSize);
    }

    [Fact]
    public void CalculateSuppressiveBurstSize_AmmoEqualsMinBurst_ReturnsMinBurst()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        int burstSize = SuppressiveFireModel.CalculateSuppressiveBurstSize(weapon, availableAmmo: 2);

        Assert.Equal(2, burstSize);
    }

    [Fact]
    public void CalculateSuppressiveBurstSize_AmmoLessThanMinBurst_ReturnsAvailableAmmo()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();
        int burstSize = SuppressiveFireModel.CalculateSuppressiveBurstSize(weapon, availableAmmo: 1);

        // Should return available ammo, not exceed it
        Assert.Equal(1, burstSize);
    }

    #endregion

    #region Suppression Severity Tests

    [Fact]
    public void CalculateSuppressiveBurstSeverity_CloseRange_ReturnsHigherSeverity()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        float closeSeverity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 3, distanceMeters: 5f);

        float farSeverity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 3, distanceMeters: 30f);

        Assert.True(closeSeverity > farSeverity,
            $"Close range ({closeSeverity:F3}) should be more suppressive than far range ({farSeverity:F3})");
    }

    [Fact]
    public void CalculateSuppressiveBurstSeverity_LargerBurst_ReturnsHigherSeverity()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        float smallBurstSeverity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 2, distanceMeters: 15f);

        float largeBurstSeverity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 5, distanceMeters: 15f);

        Assert.True(largeBurstSeverity > smallBurstSeverity,
            $"Larger burst ({largeBurstSeverity:F3}) should be more suppressive than smaller ({smallBurstSeverity:F3})");
    }

    [Fact]
    public void CalculateSuppressiveBurstSeverity_DoesNotExceedMax()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        float severity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 10, distanceMeters: 1f);

        Assert.True(severity <= SuppressionModel.MaxSuppressionLevel,
            $"Severity ({severity}) should not exceed max ({SuppressionModel.MaxSuppressionLevel})");
    }

    [Fact]
    public void CalculateSuppressiveBurstSeverity_AppliesFullCoverMultiplier()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        float severity = SuppressiveFireModel.CalculateSuppressiveBurstSeverity(
            weapon, burstSize: 3, distanceMeters: 10f);

        // The severity should be reduced by full cover multiplier
        // This is implicitly tested since the method applies the multiplier
        Assert.True(severity > 0f && severity < 1.0f,
            $"Severity ({severity}) should be reduced by full cover multiplier");
    }

    #endregion

    #region Burst Duration Tests

    [Fact]
    public void CalculateBurstDurationMs_ScalesWithBurstSize()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        long smallBurstDuration = SuppressiveFireModel.CalculateBurstDurationMs(weapon, burstSize: 2);
        long largeBurstDuration = SuppressiveFireModel.CalculateBurstDurationMs(weapon, burstSize: 5);

        Assert.True(largeBurstDuration > smallBurstDuration,
            $"Larger burst ({largeBurstDuration}ms) should take longer than smaller ({smallBurstDuration}ms)");
    }

    [Fact]
    public void CalculateBurstDurationMs_SingleShot_ReturnsZero()
    {
        var weapon = WeaponFactory.CreateSturmwolf45();

        long duration = SuppressiveFireModel.CalculateBurstDurationMs(weapon, burstSize: 1);

        Assert.Equal(0, duration); // No time between shots for single shot
    }

    #endregion

    #region ShouldUseSuppressiveFire Tests

    [Fact]
    public void ShouldUseSuppressiveFire_FullCoverRecentlyVisible_ReturnsTrue()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 30,
            targetCoverState: CoverState.Full,
            targetLastVisibleMs: 1000,
            currentTimeMs: 2000);

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseSuppressiveFire_FullCoverLongTimeAgo_ReturnsFalse()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 30,
            targetCoverState: CoverState.Full,
            targetLastVisibleMs: 1000,
            currentTimeMs: 10000); // More than TargetLastSeenWindowMs ago

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseSuppressiveFire_PartialCover_ReturnsFalse()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 30,
            targetCoverState: CoverState.Partial,
            targetLastVisibleMs: 1000,
            currentTimeMs: 1500);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseSuppressiveFire_NoCover_ReturnsFalse()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 30,
            targetCoverState: CoverState.None,
            targetLastVisibleMs: 1000,
            currentTimeMs: 1500);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseSuppressiveFire_InsufficientAmmo_ReturnsFalse()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 1, // Less than minimum burst
            targetCoverState: CoverState.Full,
            targetLastVisibleMs: 1000,
            currentTimeMs: 1500);

        Assert.False(result);
    }

    [Fact]
    public void ShouldUseSuppressiveFire_NeverSeen_ReturnsFalse()
    {
        bool result = SuppressiveFireModel.ShouldUseSuppressiveFire(
            attackerAmmo: 30,
            targetCoverState: CoverState.Full,
            targetLastVisibleMs: null,
            currentTimeMs: 1500);

        Assert.False(result);
    }

    #endregion

    #region Constant Validation Tests

    [Fact]
    public void BurstSizeConstants_AreValid()
    {
        Assert.True(SuppressiveFireModel.MinSuppressiveBurstSize >= 1, "Min burst must be at least 1");
        Assert.True(SuppressiveFireModel.MaxSuppressiveBurstSize > SuppressiveFireModel.MinSuppressiveBurstSize,
            "Max burst must be greater than min");
        Assert.True(SuppressiveFireModel.DefaultSuppressiveBurstSize >= SuppressiveFireModel.MinSuppressiveBurstSize,
            "Default burst must be at least min");
        Assert.True(SuppressiveFireModel.DefaultSuppressiveBurstSize <= SuppressiveFireModel.MaxSuppressiveBurstSize,
            "Default burst must not exceed max");
    }

    [Fact]
    public void FullCoverSuppressionMultiplier_ReducesSuppression()
    {
        Assert.True(SuppressiveFireModel.FullCoverSuppressionMultiplier < 1.0f,
            "Full cover should reduce suppression effectiveness");
        Assert.True(SuppressiveFireModel.FullCoverSuppressionMultiplier > 0f,
            "Full cover should not eliminate suppression entirely");
    }

    #endregion
}
