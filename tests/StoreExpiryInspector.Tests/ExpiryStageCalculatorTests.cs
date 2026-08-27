using StoreExpiryInspector.Domain;
using Xunit;

namespace StoreExpiryInspector.Tests;

public sealed class ExpiryStageCalculatorTests
{
    private static readonly DateOnly ExpiryDate = new(2026, 12, 31);

    [Theory]
    [InlineData(31, "none", 30)]
    [InlineData(30, "discount_50", 14)]
    [InlineData(29, "discount_50", 14)]
    [InlineData(15, "discount_50", 14)]
    [InlineData(14, "discount_20", 7)]
    [InlineData(13, "discount_20", 7)]
    [InlineData(8, "discount_20", 7)]
    [InlineData(7, "withdraw", 0)]
    [InlineData(6, "withdraw", 0)]
    [InlineData(1, "withdraw", 0)]
    [InlineData(0, "expired", -1)]
    [InlineData(-1, "expired", -1)]
    public void ShortShelfLifeUsesEveryBoundaryAndMatchingTrigger(
        int remainingDays,
        string expectedStage,
        int triggerOffset)
    {
        AssertBoundary(270, "D", remainingDays, expectedStage, triggerOffset);
    }

    [Theory]
    [InlineData(91, "none", 90)]
    [InlineData(90, "discount_50", 60)]
    [InlineData(89, "discount_50", 60)]
    [InlineData(61, "discount_50", 60)]
    [InlineData(60, "discount_20", 14)]
    [InlineData(59, "discount_20", 14)]
    [InlineData(15, "discount_20", 14)]
    [InlineData(14, "withdraw", 0)]
    [InlineData(13, "withdraw", 0)]
    [InlineData(1, "withdraw", 0)]
    [InlineData(0, "expired", -1)]
    [InlineData(-1, "expired", -1)]
    public void LongShelfLifeUsesEveryBoundaryAndMatchingTrigger(
        int remainingDays,
        string expectedStage,
        int triggerOffset)
    {
        AssertBoundary(271, "D", remainingDays, expectedStage, triggerOffset);
    }

    [Fact]
    public void TwoHundredSeventyDaysUsesShortRuleAndTwoHundredSeventyOneUsesLongRule()
    {
        var businessDate = ExpiryDate.AddDays(-30);

        var shortResult = ExpiryStageCalculator.Calculate(businessDate, ExpiryDate, 270, "D");
        var longResult = ExpiryStageCalculator.Calculate(businessDate, ExpiryDate, 271, "D");

        Assert.Equal("discount_50", shortResult.CurrentStage);
        Assert.Equal(new DateOnly(2026, 12, 17), shortResult.NextTriggerDate);
        Assert.Equal("discount_20", longResult.CurrentStage);
        Assert.Equal(new DateOnly(2026, 12, 17), longResult.NextTriggerDate);
    }

    [Fact]
    public void NineMonthsEqualsTwoHundredSeventyDays()
    {
        var result = ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-30), ExpiryDate, 9, "M");

        Assert.Equal("discount_50", result.CurrentStage);
        Assert.Equal(new DateOnly(2026, 12, 17), result.NextTriggerDate);
    }

    [Theory]
    [InlineData("D", 10, 60, "none", 30)]
    [InlineData("M", 10, 60, "discount_20", 14)]
    [InlineData("Y", 1, 60, "discount_20", 14)]
    public void SupportsDaysMonthsAndYears(
        string shelfLifeUnit,
        int shelfLifeValue,
        int remainingDays,
        string expectedStage,
        int triggerOffset)
    {
        AssertBoundary(shelfLifeValue, shelfLifeUnit, remainingDays, expectedStage, triggerOffset);
    }

    [Fact]
    public void ExpiryDateItselfIsExpiredWithoutNextTrigger()
    {
        var result = ExpiryStageCalculator.Calculate(ExpiryDate, ExpiryDate, 270, "D");

        Assert.Equal("expired", result.CurrentStage);
        Assert.Null(result.NextTriggerDate);
    }

    [Fact]
    public void UsesNaturalCalendarDaysAcrossMonthAndYearBoundaries()
    {
        var monthResult = ExpiryStageCalculator.Calculate(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 3, 5),
            270,
            "D");
        var yearResult = ExpiryStageCalculator.Calculate(
            new DateOnly(2026, 12, 31),
            new DateOnly(2027, 2, 1),
            270,
            "D");

        Assert.Equal("none", monthResult.CurrentStage);
        Assert.Equal(new DateOnly(2026, 2, 3), monthResult.NextTriggerDate);
        Assert.Equal("none", yearResult.CurrentStage);
        Assert.Equal(new DateOnly(2027, 1, 2), yearResult.NextTriggerDate);
    }

    [Fact]
    public void DoesNotRequireAProductionDate()
    {
        var result = ExpiryStageCalculator.Calculate(
            new DateOnly(2026, 11, 30),
            ExpiryDate,
            270,
            "D");

        Assert.Equal("none", result.CurrentStage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("d")]
    [InlineData("M ")]
    [InlineData(" Y")]
    [InlineData("Q")]
    [InlineData("normal")]
    public void RejectsInvalidShelfLifeUnits(string shelfLifeUnit)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, 270, shelfLifeUnit));

        Assert.Equal("shelfLifeUnit", exception.ParamName);
    }

    [Fact]
    public void RejectsNullShelfLifeUnit()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, 270, null!));

        Assert.Equal("shelfLifeUnit", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsNonPositiveShelfLifeValues(int shelfLifeValue)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, shelfLifeValue, "D"));

        Assert.Equal("shelfLifeValue", exception.ParamName);
    }

    [Fact]
    public void RejectsMonthMultiplicationOverflow()
    {
        Assert.Throws<OverflowException>(() =>
            ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, int.MaxValue, "M"));
    }

    [Fact]
    public void RejectsYearMultiplicationOverflow()
    {
        Assert.Throws<OverflowException>(() =>
            ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, int.MaxValue, "Y"));
    }

    [Fact]
    public void CalculatesTheCurrentStageDirectlyAfterMultipleMissedBoundaries()
    {
        var result = ExpiryStageCalculator.Calculate(
            ExpiryDate.AddDays(3),
            ExpiryDate,
            270,
            "D");

        Assert.Equal("expired", result.CurrentStage);
        Assert.Null(result.NextTriggerDate);
    }

    [Fact]
    public void RepeatedCallsWithTheSameInputAreDeterministic()
    {
        var first = ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, 270, "D");
        var second = ExpiryStageCalculator.Calculate(ExpiryDate.AddDays(-31), ExpiryDate, 270, "D");

        Assert.Equal(first, second);
    }

    private static void AssertBoundary(
        int shelfLifeValue,
        string shelfLifeUnit,
        int remainingDays,
        string expectedStage,
        int triggerOffset)
    {
        var businessDate = ExpiryDate.AddDays(-remainingDays);
        var result = ExpiryStageCalculator.Calculate(
            businessDate,
            ExpiryDate,
            shelfLifeValue,
            shelfLifeUnit);

        Assert.Equal(expectedStage, result.CurrentStage);
        if (expectedStage == "expired")
        {
            Assert.Null(result.NextTriggerDate);
            return;
        }

        Assert.Equal(ExpiryDate.AddDays(-triggerOffset), result.NextTriggerDate);
        Assert.True(result.NextTriggerDate!.Value > businessDate);
    }
}
