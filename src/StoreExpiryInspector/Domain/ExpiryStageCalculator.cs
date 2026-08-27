namespace StoreExpiryInspector.Domain;

public sealed record ExpiryStageResult(string CurrentStage, DateOnly? NextTriggerDate);

public static class ExpiryStageCalculator
{
    public const string None = "none";
    public const string Discount50 = "discount_50";
    public const string Discount20 = "discount_20";
    public const string Withdraw = "withdraw";
    public const string Expired = "expired";

    public static ExpiryStageResult Calculate(
        DateOnly businessDate,
        DateOnly expiryDate,
        int shelfLifeValue,
        string shelfLifeUnit)
    {
        if (shelfLifeValue <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shelfLifeValue),
                shelfLifeValue,
                "Shelf life value must be greater than zero.");
        }

        var totalShelfLifeDays = shelfLifeUnit switch
        {
            "D" => shelfLifeValue,
            "M" => checked(shelfLifeValue * 30),
            "Y" => checked(shelfLifeValue * 365),
            _ => throw new ArgumentException(
                "Shelf life unit must be exactly D, M, or Y.",
                nameof(shelfLifeUnit))
        };

        var remainingDays = expiryDate.DayNumber - businessDate.DayNumber;
        var isLongShelfLife = totalShelfLifeDays > 270;
        var firstThreshold = isLongShelfLife ? 90 : 30;
        var secondThreshold = isLongShelfLife ? 60 : 14;
        var thirdThreshold = isLongShelfLife ? 14 : 7;

        var currentStage = remainingDays <= 0
            ? Expired
            : remainingDays > firstThreshold
                ? None
                : remainingDays > secondThreshold
                    ? Discount50
                    : remainingDays > thirdThreshold
                        ? Discount20
                        : Withdraw;

        DateOnly? nextTriggerDate = currentStage switch
        {
            None => expiryDate.AddDays(-firstThreshold),
            Discount50 => expiryDate.AddDays(-secondThreshold),
            Discount20 => expiryDate.AddDays(-thirdThreshold),
            Withdraw => expiryDate,
            _ => null
        };

        return new ExpiryStageResult(currentStage, nextTriggerDate);
    }
}
