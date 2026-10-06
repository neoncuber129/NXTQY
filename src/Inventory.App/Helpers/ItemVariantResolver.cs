using Inventory.Domain.Models;
using Inventory.Domain.Services;

namespace Inventory.App.Helpers;

public enum ItemVariantResolutionKind
{
    /// <summary>Không trùng tên+giá với ai — tạo mới im lặng.</summary>
    NewItem,

    /// <summary>Khớp đủ 4 khóa với đúng một mặt hàng — gắn mã im lặng.</summary>
    ExactMatch,

    /// <summary>Trùng tên+giá với ≥1 mặt hàng nhưng chưa chọn rõ biến thể.</summary>
    Ambiguous,

    /// <summary>Mã trên dòng đã có trong danh mục — dùng mã đó.</summary>
    BoundByCode
}

public sealed record ItemVariantResolution(
    ItemVariantResolutionKind Kind,
    Item? ExactItem,
    IReadOnlyList<Item> Candidates);

/// <summary>Phát hiện dòng phiếu/tồn đầu cần hỏi chọn biến thể.</summary>
public static class ItemVariantResolver
{
    public static ItemVariantResolution Resolve(
        IReadOnlyList<Item> catalog,
        string? itemCode,
        string? itemName,
        decimal? unitPrice,
        string? category,
        string? usageType,
        bool forceCreateNewVariant = false)
    {
        if (forceCreateNewVariant)
        {
            return new ItemVariantResolution(ItemVariantResolutionKind.NewItem, null, []);
        }

        var code = itemCode?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(code))
        {
            var byCode = catalog.FirstOrDefault(x =>
                x.ItemCode.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (byCode is not null)
            {
                // Nếu tên mặt hàng đã sửa khác với tên trong catalog của mã này -> bỏ qua mã cũ, so sánh theo tên mới
                var nameDiffers = !string.IsNullOrWhiteSpace(itemName)
                    && !byCode.ItemName.Equals(itemName.Trim(), StringComparison.OrdinalIgnoreCase);

                if (!nameDiffers)
                {
                    // Mã khớp & Tên khớp nhưng danh mục/loại trên dòng khác → hỏi (dùng thuộc tính mã cũ hay tạo mới).
                    if (!string.IsNullOrWhiteSpace(itemName)
                        && unitPrice is >= 0
                        && !ItemVariantKey.Matches(byCode, itemName, unitPrice.Value, category, usageType))
                    {
                        var candidates = catalog
                            .Where(x => ItemVariantKey.SameNameAndPrice(x, itemName, unitPrice.Value))
                            .OrderBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (candidates.Count == 0)
                        {
                            candidates = [byCode];
                        }

                        return new ItemVariantResolution(ItemVariantResolutionKind.Ambiguous, byCode, candidates);
                    }

                    return new ItemVariantResolution(ItemVariantResolutionKind.BoundByCode, byCode, [byCode]);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(itemName) || unitPrice is null || unitPrice < 0)
        {
            return new ItemVariantResolution(ItemVariantResolutionKind.NewItem, null, []);
        }

        var name = itemName.Trim();
        var price = unitPrice.Value;
        var sameNamePrice = catalog
            .Where(x => ItemVariantKey.SameNameAndPrice(x, name, price))
            .OrderBy(x => x.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sameNamePrice.Count == 0)
        {
            return new ItemVariantResolution(ItemVariantResolutionKind.NewItem, null, []);
        }

        var exact = sameNamePrice.FirstOrDefault(x =>
            ItemVariantKey.Matches(x, name, price, category, usageType));
        if (exact is not null && sameNamePrice.Count == 1)
        {
            return new ItemVariantResolution(ItemVariantResolutionKind.ExactMatch, exact, sameNamePrice);
        }

        if (exact is not null
            && string.Equals(
                ItemVariantKey.NormalizeCategory(category),
                ItemVariantKey.NormalizeCategory(exact.Category),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                ItemVariantKey.NormalizeUsageType(usageType),
                ItemVariantKey.NormalizeUsageType(exact.UsageType),
                StringComparison.Ordinal))
        {
            // Đã khớp đủ 4 khóa dù còn biến thể khác cùng tên+giá — không mơ hồ.
            return new ItemVariantResolution(ItemVariantResolutionKind.ExactMatch, exact, sameNamePrice);
        }

        return new ItemVariantResolution(ItemVariantResolutionKind.Ambiguous, exact, sameNamePrice);
    }

    public static bool NeedsUserChoice(ItemVariantResolution resolution) =>
        resolution.Kind == ItemVariantResolutionKind.Ambiguous;
}
