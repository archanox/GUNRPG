using GUNRPG.Core.Combat;
using GUNRPG.Core.Operators;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for the Response Proficiency system.
/// Validates that response proficiency affects action commitment costs (transition delays)
/// without introducing randomness or AI special-casing.
/// </summary>
public class ResponseProficiencyTests
{
    #region Operator Tests

    [Fact]
    public void Operator_DefaultResponseProficiency_IsMidRange()
    {
        var op = new Operator("Test");
        
        // Default proficiency should be 0.5 (mid-range)
        Assert.Equal(0.5f, op.ResponseProficiency);
    }

    [Fact]
    public void ResponseProficiency_ClampedToValidRange()
    {
        var op = new Operator("Test");
        
        op.ResponseProficiency = 1.5f;
        Assert.Equal(1.0f, op.ResponseProficiency);
        
        op.ResponseProficiency = -0.5f;
        Assert.Equal(0.0f, op.ResponseProficiency);
    }

    #endregion

    #region Model Tests - Delay Calculation

    [Fact]
    public void CalculateEffectiveDelay_LowProficiency_IncreasesDelay()
    {
        float baseDelay = 100f;
        
        // At proficiency 0.0: delay = 100 * 1.3 = 130ms
        float effectiveDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, 0f);
        
        Assert.Equal(130f, effectiveDelay, 1);
    }

    [Fact]
    public void CalculateEffectiveDelay_MidProficiency_NeutralDelay()
    {
        float baseDelay = 100f;
        
        // At proficiency 0.5: delay = 100 * 1.0 = 100ms
        float effectiveDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, 0.5f);
        
        Assert.Equal(100f, effectiveDelay, 1);
    }

    [Fact]
    public void CalculateEffectiveDelay_HighProficiency_DecreasesDelay()
    {
        float baseDelay = 100f;
        
        // At proficiency 1.0: delay = 100 * 0.7 = 70ms
        float effectiveDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, 1f);
        
        Assert.Equal(70f, effectiveDelay, 1);
    }

    [Fact]
    public void CalculateEffectiveDelay_EnforcesMinimumDelay()
    {
        float baseDelay = 5f; // Very short delay
        
        // Even with high proficiency, should not go below MinEffectiveDelayMs (10ms)
        float effectiveDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, 1f);
        
        Assert.Equal(ResponseProficiencyModel.MinEffectiveDelayMs, effectiveDelay);
    }

    [Fact]
    public void CalculateEffectiveDelay_IsDeterministic()
    {
        float baseDelay = 150f;
        float proficiency = 0.75f;
        
        // Multiple calls should return the same result
        float result1 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, proficiency);
        float result2 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, proficiency);
        float result3 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, proficiency);
        
        Assert.Equal(result1, result2);
        Assert.Equal(result2, result3);
    }

    [Fact]
    public void CalculateEffectiveDelay_HigherProficiency_AlwaysLowerOrEqualDelay()
    {
        float baseDelay = 100f;
        
        // Test across the proficiency range
        for (float prof = 0f; prof <= 0.9f; prof += 0.1f)
        {
            float lowerProfDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, prof);
            float higherProfDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, prof + 0.1f);
            
            Assert.True(higherProfDelay <= lowerProfDelay,
                $"Higher proficiency ({prof + 0.1f}) should not increase delay. " +
                $"Low prof delay: {lowerProfDelay}, High prof delay: {higherProfDelay}");
        }
    }

    #endregion

    #region Model Tests - Multiplier

    [Fact]
    public void GetDelayMultiplier_AtExtremes()
    {
        // At 0.0 proficiency: multiplier should be MaxDelayPenaltyMultiplier (1.3)
        float lowProfMultiplier = ResponseProficiencyModel.GetDelayMultiplier(0f);
        Assert.Equal(ResponseProficiencyModel.MaxDelayPenaltyMultiplier, lowProfMultiplier, 3);
        
        // At 1.0 proficiency: multiplier should be MinDelayPenaltyMultiplier (0.7)
        float highProfMultiplier = ResponseProficiencyModel.GetDelayMultiplier(1f);
        Assert.Equal(ResponseProficiencyModel.MinDelayPenaltyMultiplier, highProfMultiplier, 3);
    }

    [Fact]
    public void GetDelayMultiplier_AtNeutral()
    {
        // At neutral proficiency (0.5): multiplier should be approximately 1.0
        float neutralMultiplier = ResponseProficiencyModel.GetDelayMultiplier(ResponseProficiencyModel.NeutralProficiency);
        Assert.Equal(1.0f, neutralMultiplier, 2);
    }

    [Fact]
    public void CalculateEffectiveDelayWithMultiplier_ReturnsConsistentValues()
    {
        float baseDelay = 100f;
        float proficiency = 0.25f;
        
        var (effectiveDelay, multiplier) = ResponseProficiencyModel.CalculateEffectiveDelayWithMultiplier(baseDelay, proficiency);
        float standaloneMultiplier = ResponseProficiencyModel.GetDelayMultiplier(proficiency);
        float standaloneDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, proficiency);
        
        Assert.Equal(standaloneMultiplier, multiplier, 3);
        Assert.Equal(standaloneDelay, effectiveDelay, 1);
    }

    #endregion

    #region Model Tests - Suppression Decay

    [Fact]
    public void CalculateEffectiveSuppressionDecayRate_HighProficiency_FasterDecay()
    {
        float baseDecayRate = 0.8f;
        
        float lowProfDecay = ResponseProficiencyModel.CalculateEffectiveSuppressionDecayRate(baseDecayRate, 0f);
        float highProfDecay = ResponseProficiencyModel.CalculateEffectiveSuppressionDecayRate(baseDecayRate, 1f);
        
        Assert.True(highProfDecay > lowProfDecay,
            $"High proficiency decay ({highProfDecay}) should be faster than low proficiency ({lowProfDecay})");
    }

    [Fact]
    public void CalculateEffectiveSuppressionDecayRate_MidProficiency_NeutralDecay()
    {
        float baseDecayRate = 0.8f;
        
        // At 0.5 proficiency, decay rate should be close to base rate
        float midProfDecay = ResponseProficiencyModel.CalculateEffectiveSuppressionDecayRate(baseDecayRate, 0.5f);
        
        Assert.Equal(baseDecayRate, midProfDecay, 2);
    }

    #endregion

    #region Integration Tests - Player vs AI Parity

    [Fact]
    public void PlayerAndAI_SameStats_IdenticalDelays()
    {
        // Create a "player" and "AI" operator with identical stats
        var player = new Operator("Player")
        {
            ResponseProficiency = 0.7f
        };
        
        var ai = new Operator("AI")
        {
            ResponseProficiency = 0.7f
        };
        
        float baseDelay = 150f;
        
        // Both should get the same delay
        float playerDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, player.ResponseProficiency);
        float aiDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, ai.ResponseProficiency);
        
        Assert.Equal(playerDelay, aiDelay);
    }

    [Fact]
    public void SkilledOperator_FeelsSmoother_ThanUnskilledOperator()
    {
        var skilled = new Operator("Skilled") { ResponseProficiency = 0.9f };
        var unskilled = new Operator("Unskilled") { ResponseProficiency = 0.1f };
        
        float baseCoverDelay = CoverTransitionModel.NoneToPartialDelayMs;
        float baseSprintToFireDelay = 120f; // Typical sprint-to-fire delay
        
        float skilledCoverDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseCoverDelay, skilled.ResponseProficiency);
        float unskilledCoverDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseCoverDelay, unskilled.ResponseProficiency);
        
        float skilledSprintDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseSprintToFireDelay, skilled.ResponseProficiency);
        float unskilledSprintDelay = ResponseProficiencyModel.CalculateEffectiveDelay(baseSprintToFireDelay, unskilled.ResponseProficiency);
        
        // Skilled operator should have noticeably lower delays
        Assert.True(skilledCoverDelay < unskilledCoverDelay * 0.8f,
            $"Skilled cover delay ({skilledCoverDelay}ms) should be significantly less than unskilled ({unskilledCoverDelay}ms)");
        
        Assert.True(skilledSprintDelay < unskilledSprintDelay * 0.8f,
            $"Skilled sprint delay ({skilledSprintDelay}ms) should be significantly less than unskilled ({unskilledSprintDelay}ms)");
    }

    [Fact]
    public void UnskilledOperator_FeelsClunky_ButDeterministic()
    {
        var clunkyOp = new Operator("Clunky") { ResponseProficiency = 0.0f };
        
        float baseDelay = 100f;
        
        // Should consistently have the maximum penalty
        float delay1 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, clunkyOp.ResponseProficiency);
        float delay2 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, clunkyOp.ResponseProficiency);
        float delay3 = ResponseProficiencyModel.CalculateEffectiveDelay(baseDelay, clunkyOp.ResponseProficiency);
        
        // All should be the same (deterministic)
        Assert.Equal(delay1, delay2);
        Assert.Equal(delay2, delay3);
        
        // Should be at maximum penalty (1.3x)
        Assert.Equal(baseDelay * ResponseProficiencyModel.MaxDelayPenaltyMultiplier, delay1, 1);
    }

    #endregion

    #region Constants Validation

    [Fact]
    public void Constants_AreWithinReasonableRange()
    {
        // Max penalty should increase delays (> 1.0)
        Assert.True(ResponseProficiencyModel.MaxDelayPenaltyMultiplier > 1.0f,
            "Max penalty multiplier should increase delays");
        
        // Min penalty should decrease delays (< 1.0)
        Assert.True(ResponseProficiencyModel.MinDelayPenaltyMultiplier < 1.0f,
            "Min penalty multiplier should decrease delays");
        
        // Minimum effective delay should be positive
        Assert.True(ResponseProficiencyModel.MinEffectiveDelayMs > 0f,
            "Minimum effective delay must be positive");
        
        // Neutral proficiency should be between 0 and 1
        Assert.InRange(ResponseProficiencyModel.NeutralProficiency, 0f, 1f);
    }

    [Fact]
    public void SkillTriangle_AllProficienciesExist()
    {
        var op = new Operator("Test");
        
        // Accuracy Proficiency (how well actions perform)
        Assert.True(op.AccuracyProficiency >= 0f && op.AccuracyProficiency <= 1f);
        
        // Response Proficiency (how fast actions switch)
        Assert.True(op.ResponseProficiency >= 0f && op.ResponseProficiency <= 1f);
        
        // Note: Reaction Proficiency is modeled via AccuracyProficiency's effect on recognition delay
        // This is verified in AwarenessModel tests
    }

    #endregion

    #region Cover Transition Integration Tests

    [Fact]
    public void CoverTransitionModel_EffectiveDelay_ScalesWithProficiency()
    {
        // Test that CoverTransitionModel.GetEffectiveTransitionDelayMs properly integrates
        // with ResponseProficiencyModel
        var fromCover = CoverState.None;
        var toCover = CoverState.Partial;

        int baseDelay = CoverTransitionModel.GetTransitionDelayMs(fromCover, toCover);
        
        int lowProfDelay = CoverTransitionModel.GetEffectiveTransitionDelayMs(fromCover, toCover, 0f);
        int midProfDelay = CoverTransitionModel.GetEffectiveTransitionDelayMs(fromCover, toCover, 0.5f);
        int highProfDelay = CoverTransitionModel.GetEffectiveTransitionDelayMs(fromCover, toCover, 1f);
        
        // Verify scaling order
        Assert.True(lowProfDelay >= midProfDelay, 
            $"Low prof delay ({lowProfDelay}) should be >= mid ({midProfDelay})");
        Assert.True(midProfDelay >= highProfDelay, 
            $"Mid prof delay ({midProfDelay}) should be >= high ({highProfDelay})");
        
        // Mid proficiency should be close to base delay (within 5ms)
        Assert.True(Math.Abs(baseDelay - midProfDelay) <= 5,
            $"Mid proficiency delay ({midProfDelay}) should be close to base delay ({baseDelay})");
    }

    [Fact]
    public void CoverTransitionModel_EffectiveDelayWithInfo_ReturnsConsistentData()
    {
        var fromCover = CoverState.Full;
        var toCover = CoverState.Partial;
        float proficiency = 0.75f;

        var (effectiveDelayMs, baseDelayMs, multiplier) = 
            CoverTransitionModel.GetEffectiveTransitionDelayWithInfo(fromCover, toCover, proficiency);
        
        // Verify values are consistent
        int expectedEffective = CoverTransitionModel.GetEffectiveTransitionDelayMs(fromCover, toCover, proficiency);
        int expectedBase = CoverTransitionModel.GetTransitionDelayMs(fromCover, toCover);
        float expectedMultiplier = ResponseProficiencyModel.GetDelayMultiplier(proficiency);
        
        Assert.Equal(expectedEffective, effectiveDelayMs);
        Assert.Equal(expectedBase, baseDelayMs);
        Assert.Equal(expectedMultiplier, multiplier, 3);
    }

    #endregion

    #region Suppression Recovery Integration Tests

    [Fact]
    public void SuppressionDecay_HighResponseProficiency_RecoversFaster()
    {
        float initialSuppression = 0.5f;
        long deltaMs = 1000; // 1 second
        bool isUnderFire = false;

        float lowProfResult = SuppressionModel.ApplyDecay(
            initialSuppression, deltaMs, isUnderFire, null, 0f);
        float highProfResult = SuppressionModel.ApplyDecay(
            initialSuppression, deltaMs, isUnderFire, null, 1f);

        Assert.True(highProfResult < lowProfResult,
            $"High proficiency decay ({highProfResult}) should leave less suppression than low proficiency ({lowProfResult})");
    }

    [Fact]
    public void Operator_SuppressionDecay_UsesResponseProficiency()
    {
        var lowProfOp = new Operator("LowProf") { ResponseProficiency = 0.1f };
        var highProfOp = new Operator("HighProf") { ResponseProficiency = 0.9f };
        
        // Apply same suppression to both
        lowProfOp.ApplySuppression(0.5f, 0);
        highProfOp.ApplySuppression(0.5f, 0);
        
        Assert.Equal(lowProfOp.SuppressionLevel, highProfOp.SuppressionLevel);
        
        // Let time pass and check decay (no fire, so decay starts immediately)
        long decayTime = SuppressionModel.ContinuedFireWindowMs + 1000; // Past the fire window
        lowProfOp.UpdateSuppressionDecay(decayTime, decayTime);
        highProfOp.UpdateSuppressionDecay(decayTime, decayTime);
        
        // High proficiency should have recovered more
        Assert.True(highProfOp.SuppressionLevel < lowProfOp.SuppressionLevel,
            $"High prof suppression ({highProfOp.SuppressionLevel:F3}) should be lower than low prof ({lowProfOp.SuppressionLevel:F3})");
    }

    #endregion
}
