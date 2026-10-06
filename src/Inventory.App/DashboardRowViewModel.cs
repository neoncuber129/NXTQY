using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Inventory.Domain.Models;

namespace Inventory.App;

public sealed partial class DashboardRowViewModel : ObservableObject
{
    public int ItemId { get; }

    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string category = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string unit = string.Empty;
    [ObservableProperty] private decimal unitPrice;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageTypeDisplay))]
    private string usageType = string.Empty;
    [ObservableProperty] private decimal openingQuantity;
    [ObservableProperty] private decimal receivedQuantity;
    [ObservableProperty] private decimal issuedQuantity;
    [ObservableProperty] private decimal closingQuantity;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string? note;

    /// <summary>Tên mặt hàng đã bỏ dấu/chữ thường — dùng tìm nhanh, tính sẵn khi tải dòng.</summary>
    public string ItemNameSearchKey { get; private set; } = string.Empty;

    public DashboardRowViewModel(DashboardRow source)
    {
        ItemId = source.ItemId;
        ItemCode = source.ItemCode;
        Category = source.Category;
        ItemName = source.ItemName;
        Unit = source.Unit;
        UnitPrice = source.UnitPrice;
        UsageType = source.UsageType;
        OpeningQuantity = source.OpeningQuantity;
        ReceivedQuantity = source.ReceivedQuantity;
        IssuedQuantity = source.IssuedQuantity;
        ClosingQuantity = source.ClosingQuantity;
        Amount = source.Amount;
        Note = source.Note;
        ItemNameSearchKey = NormalizeForSearch(ItemName);
    }

    partial void OnItemNameChanged(string value) =>
        ItemNameSearchKey = NormalizeForSearch(value);

    public string UsageTypeDisplay => FormatUsageTypeDisplay(UsageType);

    public static string FormatUsageTypeDisplay(string? usageType)
    {
        if (string.IsNullOrWhiteSpace(usageType))
        {
            return "Tự mua sắm";
        }

        var t = usageType.Trim();
        return t.Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
            || t.Equals("trên cap", StringComparison.OrdinalIgnoreCase)
            ? "Trên cấp"
            : "Tự mua sắm";
    }

    public static string NormalizeForSearch(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var formD = value.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            buffer.Append(char.ToLowerInvariant(ch));
        }

        return buffer.ToString().Normalize(NormalizationForm.FormC);
    }

    public void RefreshQuantitiesFrom(DashboardRow source)
    {
        OpeningQuantity = source.OpeningQuantity;
        ReceivedQuantity = source.ReceivedQuantity;
        IssuedQuantity = source.IssuedQuantity;
        ClosingQuantity = source.ClosingQuantity;
        Amount = source.Amount;
        UnitPrice = source.UnitPrice;
    }
}
