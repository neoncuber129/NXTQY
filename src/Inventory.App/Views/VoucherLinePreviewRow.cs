namespace Inventory.App.Views;

public sealed class VoucherLinePreviewRow
{
    public int RowNumber { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public decimal TotalAtIssueDate { get; init; }
    public decimal RemainingAtIssueDate { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Amount { get; init; }
    public string? Note { get; init; }
    /// <summary>Số lượng theo từng đơn vị trực thuộc (cùng thứ tự với danh sách đơn vị trên form).</summary>
    public IReadOnlyList<decimal?> UnitQuantities { get; init; } = [];
}
