namespace StoreExpiryInspector.Domain;

public static class ColdStartDispositions
{
    public const string Discount50Baseline = "discount50_baseline";
    public const string Discount20Baseline = "discount20_baseline";
    public const string WithdrawTask = "withdraw_task";
    public const string ExpiredTodayTask = "expired_today_task";
    public const string ExpiredCatchupTask = "expired_catchup_task";
    public const string ExpiredHistoricalBaseline = "expired_historical_baseline";
    public const string StockZeroBaseline = "stock_zero_baseline";
}
