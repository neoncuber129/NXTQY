using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Domain.Models;

/// <summary>Dashboard metadata: danh mục, loại hàng, ghi chú (gắn mã hàng; khóa tới khi có phiếu nhập sau thời điểm sửa).</summary>
public sealed record ItemMetadataPatch(int ItemId, string Category, string UsageType, string? Note);

public sealed class Item
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string UsageType { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }
    /// <summary>
    /// Thời điểm người dùng gắn/sửa ghi chú trên Dashboard.
    /// Phiếu nhập (hoặc tồn đầu) chỉ ghi đè Note khi nguồn có thời điểm sau giá trị này; sau khi ghi đè thì về null.
    /// </summary>
    public DateTime? NoteManualAt { get; set; }
}

public sealed class Receipt : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; }
    public string DeliveredBy { get; set; } = string.Empty;
    public string DeliveredUnit { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string TotalAmountText { get; set; } = string.Empty;
    public List<ReceiptLine> Lines { get; set; } = [];
    [NotMapped] public decimal UpperLevelAmount { get; set; }
    [NotMapped] public decimal SelfProcuredAmount { get; set; }

    private bool _isSelected;
    [NotMapped]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ReceiptLine
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

public sealed class Issue : INotifyPropertyChanged
{
    public int Id { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public string ReceivedUnit { get; set; } = string.Empty;
    public string DeliveredBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string TotalAmountText { get; set; } = string.Empty;
    /// <summary>Null = phiếu cân đối (ảnh hưởng tồn). Có giá trị = phiếu xuất theo đơn vị sinh từ cân đối (không trừ tồn lần nữa).</summary>
    public int? ParentIssueId { get; set; }
    public int? SubordinateUnitId { get; set; }
    public List<IssueLine> Lines { get; set; } = [];
    [NotMapped] public string ReceivedUnitCode { get; set; } = string.Empty;
    [NotMapped] public decimal UpperLevelAmount { get; set; }
    [NotMapped] public decimal SelfProcuredAmount { get; set; }
    [NotMapped] public int GeneratedUnitIssueCount { get; set; }

    private bool _isSelected;
    [NotMapped]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class UnitIssueNumberDraft
{
    public int SubordinateUnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public string IssueNumber { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
}

/// <summary>Tồn của mặt hàng ngay trước và ngay sau một phiếu cân đối.</summary>
public sealed record BalanceStockSnapshot(
    decimal TotalQuantity,
    decimal RemainingQuantity);

/// <summary>Dòng tóm tắt một phiếu xuất con trong báo cáo đơn vị.</summary>
public sealed record UnitIssueReportRow(
    int IssueId,
    int ParentIssueId,
    string ParentIssueNumber,
    DateTime IssueDate,
    string IssueNumber,
    int? SubordinateUnitId,
    string UnitCode,
    string UnitName,
    string ReceivedBy,
    string DeliveredBy,
    string Reason,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmount);

/// <summary>Dòng hàng chi tiết của phiếu xuất con dùng khi xuất báo cáo.</summary>
public sealed record UnitIssueReportLine(
    int IssueId,
    string IssueNumber,
    DateTime IssueDate,
    string UnitCode,
    string UnitName,
    int ItemId,
    string ItemCode,
    string Category,
    string ItemName,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    string? Note,
    string? UsageType = null);

/// <summary>Cập nhật ngày / thông tin phụ của phiếu xuất theo đơn vị (không đổi tồn).</summary>
public sealed class UnitIssueHeaderPatch
{
    public int IssueId { get; set; }
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string ReceivedBy { get; set; } = string.Empty;
    public string DeliveredBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class IssueLine
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public List<IssueLineAllocation> Allocations { get; set; } = [];
}

public sealed class StockLedgerEntry
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public decimal QuantityIn { get; set; }
    public decimal QuantityOut { get; set; }
    public decimal BalanceAfter { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public sealed class StockPeriod
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int ItemId { get; set; }
    public decimal OpeningQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ClosingQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsManualAdjustment { get; set; }
    /// <summary>Ghi chú tồn đầu năm (nguồn cho cột Ghi chú Dashboard, cùng phiếu nhập).</summary>
    public string? Note { get; set; }
    /// <summary>Đánh dấu ẩn khỏi Dashboard trong năm này (khi chuyển kỳ loại bỏ mặt hàng tồn cuối = 0).</summary>
    public bool IsHiddenInDashboard { get; set; }
}

/// <summary>Tóm tắt phát sinh tồn / phiếu trong một năm lịch.</summary>
public sealed record YearStockActivitySummary(
    int Year,
    int PeriodCount,
    int ItemsWithMovementCount,
    decimal TotalReceived,
    decimal TotalIssued,
    int ReceiptVoucherCount,
    int BalanceIssueVoucherCount,
    int ZeroClosingCount = 0,
    int PositiveClosingCount = 0)
{
    public bool HasStockOrVoucherActivity =>
        ItemsWithMovementCount > 0
        || ReceiptVoucherCount > 0
        || BalanceIssueVoucherCount > 0;
}

public sealed class OpeningBalanceInput
{
    public string Category { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string UsageType { get; set; } = string.Empty;
    public decimal OpeningQuantity { get; set; }
    public string? Note { get; set; }
}

public sealed class TransactionLineInput
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string UsageType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Note { get; set; }
    public Dictionary<int, decimal> Allocations { get; set; } = [];
}

public sealed class ReceiptInput
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public string DeliveredBy { get; set; } = string.Empty;
    public string DeliveredUnit { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyCollection<TransactionLineInput> Lines { get; set; } = [];
}

public sealed class IssueInput
{
    public string IssueNumber { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; } = DateTime.Today;
    public string ReceivedBy { get; set; } = string.Empty;
    public string ReceivedUnit { get; set; } = string.Empty;
    public string DeliveredBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyCollection<TransactionLineInput> Lines { get; set; } = [];
}

public sealed class DashboardRow
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string UsageType { get; set; } = string.Empty;
    public decimal OpeningQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ClosingQuantity { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

/// <summary>Key/value metadata for app (reconcile version, etc.).</summary>
public sealed class AppMeta
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class SubordinateUnit
{
    public int Id { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    /// <summary>Người nhận mặc định khi in phiếu xuất theo đơn vị này.</summary>
    public string ReceivedBy { get; set; } = string.Empty;
    /// <summary>Người giao mặc định khi in phiếu xuất theo đơn vị này.</summary>
    public string DeliveredBy { get; set; } = string.Empty;
    /// <summary>Lý do xuất mặc định khi in phiếu xuất theo đơn vị này.</summary>
    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public sealed class IssueLineAllocation
{
    public int Id { get; set; }
    public int IssueLineId { get; set; }
    public int SubordinateUnitId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class ImportError
{
    public int RowNumber { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class ImportResult<T>
{
    public List<T> Data { get; } = [];
    public List<ImportError> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}
