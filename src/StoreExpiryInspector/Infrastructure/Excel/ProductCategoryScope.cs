using StoreExpiryInspector.Domain;

namespace StoreExpiryInspector.Infrastructure.Excel;

public readonly record struct ProductCategoryScope(
    string CategoryCode,
    ExpiryManagementStatus ExpiryManagementStatus,
    string? PolicyCode,
    int? PolicyVersion);

public static class ProductCategoryScopes
{
    public static string DisplayNameForCategoryCode(string categoryCode) => categoryCode switch
    {
        "food" => "食品",
        "pet" => "宠物",
        "daily_use" => "日用",
        "beauty" => "美妆",
        "home" => "家居",
        "fragrance" => "香氛香水",
        "stationery" => "文具",
        "trendy_toys" => "潮流玩具",
        "seasonal_assortment" => "应季搭配",
        "gift_sample" => "赠品小样",
        _ => throw new ArgumentOutOfRangeException(nameof(categoryCode))
    };

    public static bool IsKnown(string? category) => category?.Trim() is
        "食品" or "宠物" or "日用" or "美妆" or "家居" or "香氛香水" or "文具" or "潮流玩具" or "应季搭配" or "赠品小样";

    public static ProductCategoryScope Resolve(string category, int shelfLifeValue, string shelfLifeUnit)
    {
        var trimmed = category.Trim();
        return trimmed switch
        {
            "食品" => Managed("food", ExpiryPolicies.Food),
            "宠物" => Managed("pet", ExpiryPolicies.Pet),
            "应季搭配" => Unmanaged("seasonal_assortment", ExpiryManagementStatus.Excluded),
            "赠品小样" => Unmanaged("gift_sample", ExpiryManagementStatus.Excluded),
            "日用" => General("daily_use", shelfLifeValue, shelfLifeUnit),
            "美妆" => General("beauty", shelfLifeValue, shelfLifeUnit),
            "家居" => General("home", shelfLifeValue, shelfLifeUnit),
            "香氛香水" => General("fragrance", shelfLifeValue, shelfLifeUnit),
            "文具" => General("stationery", shelfLifeValue, shelfLifeUnit),
            "潮流玩具" => General("trendy_toys", shelfLifeValue, shelfLifeUnit),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }

    private static ProductCategoryScope General(string categoryCode, int value, string unit) =>
        ToDays(value, unit) > 180
            ? Managed(categoryCode, ExpiryPolicies.GeneralLong)
            : Unmanaged(categoryCode, ExpiryManagementStatus.Unresolved);

    private static long ToDays(int value, string unit) => unit switch
    {
        "D" => value,
        "M" => (long)value * 30,
        "Y" => (long)value * 365,
        _ => throw new ArgumentOutOfRangeException(nameof(unit))
    };

    private static ProductCategoryScope Managed(string categoryCode, string policyCode) =>
        new(categoryCode, ExpiryManagementStatus.Managed, policyCode, ExpiryPolicies.Version1);

    private static ProductCategoryScope Unmanaged(string categoryCode, ExpiryManagementStatus status) =>
        new(categoryCode, status, null, null);
}
