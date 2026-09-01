namespace StoreExpiryInspector.Domain;

public static class ExpiryPolicyCalculator
{
    public static ExpiryStageResult? Calculate(
        string policyCode,
        int policyVersion,
        DateOnly businessDate,
        DateOnly expiryDate,
        int totalShelfLifeDays)
    {
        if (totalShelfLifeDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalShelfLifeDays));
        }

        if (policyVersion != ExpiryPolicies.Version1)
        {
            throw new ArgumentOutOfRangeException(nameof(policyVersion));
        }

        var thresholds = policyCode switch
        {
            ExpiryPolicies.Food when totalShelfLifeDays <= 270 => (30, 14, 7),
            ExpiryPolicies.Food => (90, 60, 14),
            ExpiryPolicies.Pet => (90, 60, 14),
            ExpiryPolicies.GeneralLong when totalShelfLifeDays > 180 => (180, 90, 14),
            ExpiryPolicies.GeneralLong => ((int, int, int)?)null,
            _ => throw new ArgumentException("Unknown expiry policy.", nameof(policyCode))
        };
        if (thresholds is null)
        {
            return null;
        }

        var remainingDays = expiryDate.DayNumber - businessDate.DayNumber;
        var (first, second, third) = thresholds.Value;
        var stage = remainingDays <= 0 ? ExpiryStageCalculator.Expired
            : remainingDays > first ? ExpiryStageCalculator.None
            : remainingDays > second ? ExpiryStageCalculator.Discount50
            : remainingDays > third ? ExpiryStageCalculator.Discount20
            : ExpiryStageCalculator.Withdraw;
        return new(stage, stage switch
        {
            ExpiryStageCalculator.None => expiryDate.AddDays(-first),
            ExpiryStageCalculator.Discount50 => expiryDate.AddDays(-second),
            ExpiryStageCalculator.Discount20 => expiryDate.AddDays(-third),
            ExpiryStageCalculator.Withdraw => expiryDate,
            _ => null
        });
    }
}
