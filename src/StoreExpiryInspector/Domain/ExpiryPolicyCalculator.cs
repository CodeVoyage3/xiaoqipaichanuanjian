namespace StoreExpiryInspector.Domain;

public sealed record ExpiryPolicyStageDates(
    DateOnly Discount50,
    DateOnly Discount20,
    DateOnly Withdraw,
    DateOnly Expired);

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

        var dates = CalculateStageDates(policyCode, policyVersion, expiryDate, totalShelfLifeDays);
        if (dates is null)
        {
            return null;
        }

        var remainingDays = expiryDate.DayNumber - businessDate.DayNumber;
        var stage = remainingDays <= 0 ? ExpiryStageCalculator.Expired
            : businessDate < dates.Discount50 ? ExpiryStageCalculator.None
            : businessDate < dates.Discount20 ? ExpiryStageCalculator.Discount50
            : businessDate < dates.Withdraw ? ExpiryStageCalculator.Discount20
            : ExpiryStageCalculator.Withdraw;
        return new(stage, stage switch
        {
            ExpiryStageCalculator.None => dates.Discount50,
            ExpiryStageCalculator.Discount50 => dates.Discount20,
            ExpiryStageCalculator.Discount20 => dates.Withdraw,
            ExpiryStageCalculator.Withdraw => dates.Expired,
            _ => null
        });
    }

    public static ExpiryPolicyStageDates? CalculateStageDates(
        string policyCode,
        int policyVersion,
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

        var (first, second, third) = thresholds.Value;
        return new(expiryDate.AddDays(-first), expiryDate.AddDays(-second), expiryDate.AddDays(-third), expiryDate);
    }
}
