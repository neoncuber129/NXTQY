using Inventory.Domain.Models;

namespace Inventory.Domain.Services;

/// <summary>Chuẩn hóa / khóa biến thể mặt hàng (tên + giá + danh mục + loại).</summary>
public static class ItemVariantKey
{
    public static string NormalizeUsageType(string? usageType)
    {
        var t = (usageType ?? string.Empty).Trim();
        if (t.Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
            || t.Equals("trên cap", StringComparison.OrdinalIgnoreCase))
        {
            return "trên cấp";
        }

        return string.Empty;
    }

    public static string NormalizeCategory(string? category) => (category ?? string.Empty).Trim();

    public static string NormalizeName(string? name) => (name ?? string.Empty).Trim();

    public static string Build(
        string? itemName,
        decimal unitPrice,
        string? category,
        string? usageType) =>
        string.Join(
            '\u001f',
            NormalizeName(itemName).ToUpperInvariant(),
            unitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NormalizeCategory(category).ToUpperInvariant(),
            NormalizeUsageType(usageType).ToUpperInvariant());

    public static bool Matches(Item item, string? itemName, decimal unitPrice, string? category, string? usageType) =>
        string.Equals(NormalizeName(item.ItemName), NormalizeName(itemName), StringComparison.OrdinalIgnoreCase)
        && item.UnitPrice == unitPrice
        && string.Equals(NormalizeCategory(item.Category), NormalizeCategory(category), StringComparison.OrdinalIgnoreCase)
        && string.Equals(NormalizeUsageType(item.UsageType), NormalizeUsageType(usageType), StringComparison.Ordinal);

    public static bool SameNameAndPrice(Item item, string? itemName, decimal unitPrice) =>
        string.Equals(NormalizeName(item.ItemName), NormalizeName(itemName), StringComparison.OrdinalIgnoreCase)
        && item.UnitPrice == unitPrice;

    /// <summary>Mã gốc dạng Tên:Giá (không gắn nhãn biến thể).</summary>
    public static string BuildBaseItemCode(string? itemName, decimal unitPrice)
    {
        var namePart = NormalizeName(itemName);
        if (string.IsNullOrWhiteSpace(namePart))
        {
            namePart = "MH";
        }

        if (namePart.Length > 40)
        {
            namePart = namePart[..40];
        }

        var pricePart = decimal.Round(unitPrice, 0, MidpointRounding.AwayFromZero)
            .ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        var code = $"{namePart}:{pricePart}";
        return code.Length <= 50 ? code : code[..50];
    }

    /// <summary>
    /// Mã biến thể khi Tên:Giá đã bị biến thể khác chiếm: Tên:Giá|danh mục|TC.
    /// Trả null nếu không có nhãn (caller dùng Tên:Giá-2).
    /// </summary>
    public static string? BuildVariantItemCode(string? itemName, decimal unitPrice, string? category, string? usageType)
    {
        var namePart = NormalizeName(itemName);
        if (string.IsNullOrWhiteSpace(namePart))
        {
            namePart = "MH";
        }

        var pricePart = decimal.Round(unitPrice, 0, MidpointRounding.AwayFromZero)
            .ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        var tags = new List<string>();
        var catTag = SanitizeCodeTag(NormalizeCategory(category), maxLen: 12);
        if (!string.IsNullOrEmpty(catTag))
        {
            tags.Add(catTag);
        }

        if (NormalizeUsageType(usageType) == "trên cấp")
        {
            tags.Add("TC");
        }

        if (tags.Count == 0)
        {
            // Không có nhãn phân biệt → để caller dùng hậu tố -2 trên mã gốc.
            return null;
        }

        var tagPart = string.Join("|", tags);
        // Rút tên để vừa 50: name:price|tags
        var overhead = 1 + pricePart.Length + 1 + tagPart.Length; // : + price + | + tags
        var maxName = Math.Max(1, 50 - overhead);
        if (namePart.Length > maxName)
        {
            namePart = namePart[..maxName];
        }

        return $"{namePart}:{pricePart}|{tagPart}";
    }

    private static string SanitizeCodeTag(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Replace(":", "-", StringComparison.Ordinal)
            .Replace("|", "-", StringComparison.Ordinal)
            .Trim();
        if (cleaned.Length > maxLen)
        {
            cleaned = cleaned[..maxLen].TrimEnd();
        }

        return cleaned;
    }
}
