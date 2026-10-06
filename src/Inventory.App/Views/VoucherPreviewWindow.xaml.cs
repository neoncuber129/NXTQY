using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Inventory.App.Helpers;
using Inventory.Domain.Models;
using Inventory.Domain.Services;
using Inventory.Reporting.Services;
using Microsoft.Win32;

namespace Inventory.App.Views;

public partial class VoucherPreviewWindow : Window, INotifyPropertyChanged
{
    private readonly WordExportService _wordExport;
    private readonly IReadOnlyDictionary<int, Item> _itemsById;
    private readonly string _templatePath;
    private readonly bool _isReceipt;
    private readonly Receipt? _receipt;
    private readonly Issue? _issue;
    private readonly IReadOnlyList<SubordinateUnit> _subordinateUnits;
    private readonly List<IssueLine> _allIssueLines = [];
    private bool _syncingSelection;
    private bool _unitsScrollAttached;

    private async Task RunBusyAsync(string message, Action work)
    {
        BusyOverlay.Message = message;
        BusyOverlay.Visibility = Visibility.Visible;
        try
        {
            await Task.Yield();
            await Task.Run(work);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public string KindTitle { get; }
    public string DocumentNumber { get; }
    public DateTime DocumentDate { get; }
    public decimal TotalAmount { get; private set; }
    public string PrimaryPersonLabel { get; }
    public string PrimaryPerson { get; }
    public string PrimaryUnitLabel { get; }
    public string PrimaryUnit { get; private set; }
    public string SecondaryPersonLabel { get; }
    public string SecondaryPerson { get; }
    public string ReasonLabel { get; }
    public string Reason { get; }
    public bool HasUnitFilter { get; }
    public bool ShowExtraHeaderInfo { get; }
    public bool ShowWordExportButton { get; }
    public string WordActionButtonText { get; }
    public ObservableCollection<VoucherLinePreviewRow> LineRows { get; } = [];
    public ObservableCollection<UnitFilterOption> UnitFilterOptions { get; } = [];

    public UnitFilterOption? SelectedUnitFilter { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public VoucherPreviewWindow(
        WordExportService wordExport,
        Receipt receipt,
        IReadOnlyDictionary<int, Item> itemsById,
        string templatePath)
    {
        _wordExport = wordExport;
        _itemsById = itemsById;
        _templatePath = templatePath;
        _isReceipt = true;
        _receipt = receipt;
        _issue = null;
        _subordinateUnits = [];
        HasUnitFilter = false;
        ShowExtraHeaderInfo = true;
        ShowWordExportButton = true;
        WordActionButtonText = "Xuất file Word…";

        KindTitle = "Phiếu nhập";
        DocumentNumber = receipt.ReceiptNumber;
        DocumentDate = receipt.ReceiptDate;
        TotalAmount = receipt.TotalAmount;
        PrimaryPersonLabel = "Theo Kế hoạch";
        PrimaryPerson = receipt.DeliveredBy;
        PrimaryUnitLabel = "Theo Hoá đơn số";
        PrimaryUnit = receipt.DeliveredUnit;
        SecondaryPersonLabel = "Người nhận";
        SecondaryPerson = receipt.ReceivedBy;
        ReasonLabel = "Lý do nhập";
        Reason = receipt.Reason;

        InitializeComponent();
        CollapseUnitsPanel();
        FillReceiptLines(receipt.Lines.OrderBy(x => x.Id), itemsById);
        Title = $"Xem trước — Phiếu nhập {receipt.ReceiptNumber}";
        DataContext = this;
    }

    public VoucherPreviewWindow(
        WordExportService wordExport,
        Issue issue,
        IReadOnlyDictionary<int, Item> itemsById,
        string templatePath,
        IReadOnlyList<SubordinateUnit> subordinateUnits,
        bool showWordExportButton = true)
    {
        _wordExport = wordExport;
        _itemsById = itemsById;
        _templatePath = templatePath;
        _isReceipt = false;
        _receipt = null;
        _issue = issue;
        _subordinateUnits = subordinateUnits.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();
        _allIssueLines.AddRange(issue.Lines.OrderBy(x => x.Id));

        var hasAllocations = issue.Lines.Any(l => l.Allocations.Count > 0);
        HasUnitFilter = hasAllocations && _subordinateUnits.Count > 0;

        var isUnitIssue = issue.ParentIssueId is not null;
        KindTitle = isUnitIssue ? "Phiếu xuất" : "Phiếu cân đối";
        ShowExtraHeaderInfo = isUnitIssue;
        ShowWordExportButton = showWordExportButton;
        WordActionButtonText = "Xem theo mẫu Word…";
        DocumentNumber = issue.IssueNumber;
        DocumentDate = issue.IssueDate;
        TotalAmount = issue.TotalAmount;
        PrimaryPersonLabel = "Người nhận";
        PrimaryPerson = issue.ReceivedBy;
        PrimaryUnitLabel = isUnitIssue ? "Đơn vị trực thuộc" : "Đơn vị người nhận";
        PrimaryUnit = issue.ReceivedUnit;
        SecondaryPersonLabel = "Người giao";
        SecondaryPerson = issue.DeliveredBy;
        ReasonLabel = "Lý do";
        Reason = issue.Reason;

        if (HasUnitFilter)
        {
            UnitFilterOptions.Add(new UnitFilterOption(null, "Gộp tất cả đơn vị (như khi nhập phiếu)"));
            foreach (var unit in _subordinateUnits)
            {
                UnitFilterOptions.Add(new UnitFilterOption(unit.Id, unit.Name));
            }

            SelectedUnitFilter = UnitFilterOptions[0];
        }

        InitializeComponent();
        EnsureBalanceUnitColumns(showAllUnits: true);
        if (isUnitIssue || !HasUnitFilter)
        {
            FillSimpleIssueLines(_allIssueLines, itemsById);
        }
        else
        {
            ApplyUnitFilter();
        }

        Title = isUnitIssue
            ? $"Xem trước — Phiếu xuất {issue.IssueNumber}"
            : $"Xem trước — Phiếu cân đối {issue.IssueNumber}";
        DataContext = this;
    }

    private void EnsureBalanceUnitColumns(bool showAllUnits)
    {
        UnitsGrid.Columns.Clear();

        if (_isReceipt || !HasUnitFilter || !showAllUnits || _subordinateUnits.Count == 0)
        {
            QuantityColumn.Header = "Số lượng";
            CollapseUnitsPanel();
            return;
        }

        ShowUnitsPanel();
        var centerStyle = (Style)FindResource("CenterAlignedCell");
        for (var i = 0; i < _subordinateUnits.Count; i++)
        {
            var unit = _subordinateUnits[i];
            var column = new DataGridTextColumn
            {
                Header = unit.Name,
                Width = new DataGridLength(100),
                MinWidth = 80,
                IsReadOnly = true,
                ElementStyle = centerStyle,
                Binding = new Binding($"UnitQuantities[{i}]")
                {
                    Mode = BindingMode.OneWay,
                    StringFormat = "N0",
                    TargetNullValue = string.Empty
                }
            };
            UnitsGrid.Columns.Add(column);
        }

        QuantityColumn.Header = "Số lượng (tổng)";
        if (!_unitsScrollAttached)
        {
            DataGridScrollSync.Attach(LinesGrid, UnitsGrid);
            _unitsScrollAttached = true;
        }
    }

    private void CollapseUnitsPanel()
    {
        UnitsPanel.Visibility = Visibility.Collapsed;
        LinesSplitter.Visibility = Visibility.Collapsed;
        LinesSplitHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        LinesSplitHost.ColumnDefinitions[2].Width = new GridLength(0);
    }

    private void ShowUnitsPanel()
    {
        UnitsPanel.Visibility = Visibility.Visible;
        LinesSplitter.Visibility = Visibility.Visible;
        LinesSplitHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        LinesSplitHost.ColumnDefinitions[2].Width = new GridLength(1.1, GridUnitType.Star);
    }

    private void LinesGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        DataGridScrollSync.SyncFullRowSelection(LinesGrid, UnitsGrid);
        _syncingSelection = false;
    }

    private void UnitsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        DataGridScrollSync.SyncFullRowSelection(UnitsGrid, LinesGrid);
        _syncingSelection = false;
    }

    private void ApplyUnitFilter()
    {
        if (_isReceipt || _issue is null)
        {
            return;
        }

        LineRows.Clear();
        var unitId = SelectedUnitFilter?.UnitId;
        var showAllUnits = unitId is null;
        EnsureBalanceUnitColumns(showAllUnits);

        if (showAllUnits)
        {
            FillBalanceLinesCombined(_allIssueLines, _itemsById);
            TotalAmount = _issue.TotalAmount;
            PrimaryUnit = _issue.ReceivedUnit;
            Notify(nameof(TotalAmount));
            Notify(nameof(PrimaryUnit));
            return;
        }

        var filteredLines = new List<IssueLine>();
        foreach (var line in _allIssueLines)
        {
            var alloc = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == unitId!.Value && a.Quantity > 0);
            if (alloc is null)
            {
                continue;
            }

            filteredLines.Add(new IssueLine
            {
                ItemId = line.ItemId,
                Quantity = alloc.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = alloc.Quantity * line.UnitPrice,
                Note = line.Note
            });
        }

        FillSimpleIssueLines(filteredLines, _itemsById);
        TotalAmount = filteredLines.Sum(x => x.Amount);
        PrimaryUnit = _subordinateUnits.FirstOrDefault(u => u.Id == unitId!.Value)?.Name ?? _issue.ReceivedUnit;
        Notify(nameof(TotalAmount));
        Notify(nameof(PrimaryUnit));
    }

    private void FillReceiptLines(IEnumerable<ReceiptLine> lines, IReadOnlyDictionary<int, Item> itemsById)
    {
        var index = 1;
        foreach (var line in lines)
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            LineRows.Add(new VoucherLinePreviewRow
            {
                RowNumber = index++,
                ItemCode = item?.ItemCode ?? $"#{line.ItemId}",
                ItemName = item?.ItemName ?? string.Empty,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                Note = line.Note
            });
        }
    }

    private void FillBalanceLinesCombined(IEnumerable<IssueLine> lines, IReadOnlyDictionary<int, Item> itemsById)
    {
        var index = 1;
        foreach (var line in lines)
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            var unitQtys = _subordinateUnits
                .Select(u =>
                {
                    var alloc = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == u.Id);
                    return alloc is { Quantity: > 0 } ? alloc.Quantity : (decimal?)null;
                })
                .ToList();

            LineRows.Add(new VoucherLinePreviewRow
            {
                RowNumber = index++,
                ItemCode = item?.ItemCode ?? $"#{line.ItemId}",
                ItemName = item?.ItemName ?? string.Empty,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                Note = line.Note,
                UnitQuantities = unitQtys
            });
        }
    }

    private void FillSimpleIssueLines(IEnumerable<IssueLine> lines, IReadOnlyDictionary<int, Item> itemsById)
    {
        var index = 1;
        foreach (var line in lines)
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            LineRows.Add(new VoucherLinePreviewRow
            {
                RowNumber = index++,
                ItemCode = item?.ItemCode ?? $"#{line.ItemId}",
                ItemName = item?.ItemName ?? string.Empty,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                Note = line.Note
            });
        }
    }

    private async void OpenWord_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_templatePath) || !File.Exists(_templatePath))
            {
                MessageBox.Show(this, "Chưa cấu hình template Word trong Cài đặt.", "Thiếu template", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_isReceipt && _receipt is not null)
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Word (*.docx)|*.docx",
                    FileName = $"PN_{FileNameSanitizer.Sanitize(_receipt.ReceiptNumber)}.docx",
                    AddExtension = true,
                    DefaultExt = ".docx"
                };
                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                await RunBusyAsync(
                    "Đang xuất Word phiếu nhập...",
                    () => _wordExport.ExportReceipt(_templatePath, dialog.FileName, _receipt, _itemsById));
                MessageBox.Show(
                    this,
                    $"Đã xuất file Word:\n{dialog.FileName}",
                    "Xuất Word thành công",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_issue is null)
            {
                return;
            }

            if (_subordinateUnits.Count > 0)
            {
                var folder = PickFolder("Chọn thư mục lưu phiếu xuất tất cả đơn vị + sổ cân đối");
                if (folder is null)
                {
                    return;
                }

                var overviewPath = Path.Combine(
                    folder,
                    $"SoCanDoi_{FileNameSanitizer.Sanitize(_issue.IssueNumber)}.docx");
                await RunBusyAsync(
                    $"Đang xuất {_subordinateUnits.Count} phiếu đơn vị và sổ cân đối...",
                    () =>
                    {
                        foreach (var unit in _subordinateUnits)
                        {
                            var unitIssue = BuildIssueForUnit(_issue, unit);
                            var output = Path.Combine(
                                folder,
                                $"PX_{FileNameSanitizer.Sanitize(_issue.IssueNumber)}_{FileNameSanitizer.Sanitize(unit.Name)}.docx");
                            _wordExport.ExportIssue(_templatePath, output, unitIssue, _itemsById);
                        }

                        _wordExport.ExportBalanceOverview(_templatePath, overviewPath, _issue, _itemsById, _subordinateUnits);
                    });
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                return;
            }

            var combinedTemp = Path.Combine(Path.GetTempPath(), $"CD_{Guid.NewGuid():N}.docx");
            await RunBusyAsync(
                "Đang tạo file Word...",
                () => _wordExport.ExportIssue(_templatePath, combinedTemp, _issue, _itemsById));
            Process.Start(new ProcessStartInfo(combinedTemp) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                _isReceipt ? "Không xuất được Word" : "Không mở được Word",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

        private static Issue BuildIssueForUnit(Issue source, SubordinateUnit unit)
    {
        var unitIssue = new Issue
        {
            IssueNumber = source.IssueNumber,
            IssueDate = source.IssueDate,
            ReceivedBy = unit.ReceivedBy,
            ReceivedUnit = unit.Name,
            DeliveredBy = unit.DeliveredBy,
            Reason = source.Reason
        };

        foreach (var line in source.Lines)
        {
            var alloc = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == unit.Id && a.Quantity > 0);
            if (alloc is null)
            {
                continue;
            }

            unitIssue.Lines.Add(new IssueLine
            {
                ItemId = line.ItemId,
                Quantity = alloc.Quantity,
                UnitPrice = line.UnitPrice,
                Amount = alloc.Quantity * line.UnitPrice,
                Note = line.Note
            });
        }

        unitIssue.TotalAmount = unitIssue.Lines.Sum(x => x.Amount);
        unitIssue.TotalAmountText = VietnameseMoneyTextConverter.Convert(unitIssue.TotalAmount);
        return unitIssue;
    }

    private static string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void UnitFilterCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyUnitFilter();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed class UnitFilterOption(int? unitId, string displayName)
{
    public int? UnitId { get; } = unitId;
    public string DisplayName { get; } = displayName;
}
