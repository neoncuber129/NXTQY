using Inventory.App.Views;
using Inventory.Domain.Models;

namespace Inventory.App.Helpers;

public static class DuplicateUnitPriceChecker
{
    public sealed record ItemLineInput(
        int RowNumber,
        string ItemName,
        string ItemCode,
        decimal UnitPrice);

    public static IReadOnlyList<DuplicateUnitPriceGroup> FindDuplicateGroups(
        IReadOnlyList<ItemLineInput> documentLines,
        IReadOnlyList<Item> catalogItems)
    {
        var validLines = documentLines
            .Where(x => (!string.IsNullOrWhiteSpace(x.ItemName) || !string.IsNullOrWhiteSpace(x.ItemCode)) && x.UnitPrice >= 0)
            .ToList();

        if (validLines.Count == 0)
        {
            return Array.Empty<DuplicateUnitPriceGroup>();
        }

        // Gom các dòng trong phiếu theo đơn giá
        var documentByPrice = validLines
            .GroupBy(x => x.UnitPrice)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Gom danh mục catalog (dashboard) theo đơn giá
        var catalogByPrice = catalogItems
            .Where(x => x.UnitPrice >= 0)
            .GroupBy(x => x.UnitPrice)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<DuplicateUnitPriceGroup>();

        // Tất cả các đơn giá xuất hiện trong phiếu
        foreach (var priceKv in documentByPrice.OrderBy(kv => kv.Key))
        {
            var price = priceKv.Key;
            var docLinesForPrice = priceKv.Value;

            // Mặt hàng catalog có cùng đơn giá (loại bỏ các mặt hàng trùng mã với chính các dòng đã gắn mã trong phiếu nếu có)
            var docCodes = docLinesForPrice
                .Select(x => x.ItemCode?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var docNames = docLinesForPrice
                .Select(x => x.ItemName?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var catalogForPrice = catalogByPrice.TryGetValue(price, out var catList)
                ? catList.Where(item => !docCodes.Contains(item.ItemCode)).ToList()
                : [];

            // Kiểm tra trùng:
            // Trùng 1: Phiếu có >= 2 dòng có cùng đơn giá
            // Trùng 2: Phiếu có dòng trùng đơn giá với catalog (ít nhất 1 dòng phiếu và 1 dòng catalog)
            // Lưu ý: Nếu phiếu có 1 dòng, catalog có 1 mặt hàng trùng chính tên+đơn giá (chính là mặt hàng đó), ta kiểm tra if name/code là cùng 1 item thì không coi là trùng lỗi nhập.
            // Nhưng nếu catalog có mặt hàng trùng đơn giá mà tên khác hoặc biến thể khác, đó là trùng đơn giá cần nhắc.
            var inDocDisplays = docLinesForPrice
                .Select(x =>
                {
                    var name = string.IsNullOrWhiteSpace(x.ItemName) ? x.ItemCode : x.ItemName;
                    return $"Dòng {x.RowNumber}: {name}";
                })
                .Distinct()
                .ToList();

            var dashboardDisplays = catalogForPrice
                .Select(x => $"{x.ItemName} (Mã: {x.ItemCode}, Danh mục: {(string.IsNullOrWhiteSpace(x.Category) ? "chưa phân loại" : x.Category)})")
                .Distinct()
                .ToList();

            bool isDuplicateInDoc = docLinesForPrice.Count >= 2;
            bool isDuplicateWithDashboard = dashboardDisplays.Count > 0 && inDocDisplays.Count >= 1;

            if (isDuplicateInDoc || isDuplicateWithDashboard)
            {
                result.Add(new DuplicateUnitPriceGroup(
                    price,
                    inDocDisplays,
                    dashboardDisplays));
            }
        }

        return result;
    }
}
