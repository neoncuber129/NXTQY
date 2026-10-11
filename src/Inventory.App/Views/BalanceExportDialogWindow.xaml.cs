using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Inventory.Domain.Models;

namespace Inventory.App.Views;

public enum BalanceExportFormat
{
    Word,
    Excel
}

public sealed record BalanceExportResult(
    BalanceExportFormat Format,
    IReadOnlySet<int> SelectedLineIds,
    bool OmitZeroStock,
    bool HideZeroQuantity = true);

public partial class BalanceExportDialogWindow : Window
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public sealed class BalanceExportItemRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int LineId { get; init; }
        public int ItemId { get; init; }
        public int RowNumber { get; init; }
        public string ItemCode { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public decimal TotalAtIssueDate { get; init; }
        public decimal Quantity { get; init; }
        public decimal RemainingAtIssueDate { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal Amount { get; init; }
        public string Note { get; init; } = string.Empty;

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
    }

    private readonly ObservableCollection<BalanceExportItemRow> _allRows = [];
    private readonly ICollectionView _rowsView;
    private bool _suppressSelectionSync;

    public BalanceExportResult? Result { get; private set; }
    public IReadOnlyList<BalanceExportItemRow> AllRows => _allRows.ToList();
    public ICollectionView RowsView => _rowsView;
    public bool IsOmitZeroStockChecked => OmitZeroStockCheckBox.IsChecked == true;
    public bool IsHideZeroQuantityChecked => HideZeroQuantityCheckBox.IsChecked == true;

    public void SetHideZeroQuantity(bool value)
    {
        HideZeroQuantityCheckBox.IsChecked = value;
    }

    public void SetOmitZeroStock(bool value)
    {
        OmitZeroStockCheckBox.IsChecked = value;
    }

    public BalanceExportDialogWindow(
        Issue balance,
        IReadOnlyDictionary<int, Item> itemsById,
        IReadOnlyDictionary<int, BalanceStockSnapshot> stockSnapshots,
        bool? initialOmitZeroStock = null,
        bool? initialHideZeroQuantity = null)
    {
        InitializeComponent();

        var omitZero = initialOmitZeroStock ?? UiPreferenceService.LoadOmitZeroStockAtIssueDate();
        var hideZeroQuantity = initialHideZeroQuantity ?? UiPreferenceService.LoadHideZeroQuantityBalanceExport();

        HeaderText.Text = $"Sổ cân đối: {balance.IssueNumber} (Ngày lập: {balance.IssueDate:dd/MM/yyyy})";

        var rowIndex = 1;
        foreach (var line in balance.Lines.OrderBy(x => x.Id))
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            stockSnapshots.TryGetValue(line.ItemId, out var snapshot);

            var totalAtIssueDate = snapshot?.TotalQuantity ?? 0m;
            var remainingAtIssueDate = snapshot?.RemainingQuantity ?? 0m;

            var isSelected = true;
            if (hideZeroQuantity && line.Quantity <= 0)
            {
                isSelected = false;
            }
            else if (omitZero && totalAtIssueDate <= 0)
            {
                isSelected = false;
            }

            var row = new BalanceExportItemRow
            {
                LineId = line.Id,
                ItemId = line.ItemId,
                RowNumber = rowIndex++,
                ItemCode = item?.ItemCode ?? $"#{line.ItemId}",
                ItemName = item?.ItemName ?? string.Empty,
                Category = item?.Category ?? string.Empty,
                Unit = item?.Unit ?? string.Empty,
                TotalAtIssueDate = totalAtIssueDate,
                Quantity = line.Quantity,
                RemainingAtIssueDate = remainingAtIssueDate,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                Note = line.Note ?? string.Empty,
                IsSelected = isSelected
            };

            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
        }

        _rowsView = CollectionViewSource.GetDefaultView(_allRows);
        _rowsView.Filter = FilterRow;
        ItemsGrid.ItemsSource = _rowsView;

        _suppressSelectionSync = true;
        OmitZeroStockCheckBox.IsChecked = omitZero;
        HideZeroQuantityCheckBox.IsChecked = hideZeroQuantity;
        _suppressSelectionSync = false;

        _rowsView.Refresh();
        UpdateSummary();
    }

    private bool FilterRow(object item)
    {
        if (item is not BalanceExportItemRow row)
        {
            return false;
        }

        if (HideZeroQuantityCheckBox?.IsChecked == true && row.Quantity <= 0)
        {
            return false;
        }

        var filter = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        return row.ItemCode.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || row.ItemName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || row.Category.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BalanceExportItemRow.IsSelected))
        {
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var selectedCount = _allRows.Count(x => x.IsSelected);
        var totalCount = _allRows.Count;
        var selectedAmount = _allRows.Where(x => x.IsSelected).Sum(x => x.Amount);

        SummaryText.Text = $"Đã chọn: {selectedCount}/{totalCount} mặt hàng | Tổng tiền: {selectedAmount.ToString("N0", VietnameseCulture)} đ";

        if (!_suppressSelectionSync)
        {
            _suppressSelectionSync = true;
            if (selectedCount == totalCount && totalCount > 0)
            {
                HeaderSelectAllCheckBox.IsChecked = true;
            }
            else if (selectedCount == 0)
            {
                HeaderSelectAllCheckBox.IsChecked = false;
            }
            else
            {
                HeaderSelectAllCheckBox.IsChecked = null;
            }
            _suppressSelectionSync = false;
        }
    }

    private void OmitZeroStock_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        var omit = OmitZeroStockCheckBox.IsChecked == true;
        UiPreferenceService.SaveOmitZeroStockAtIssueDate(omit);

        foreach (var row in _allRows)
        {
            if (row.TotalAtIssueDate <= 0)
            {
                if (omit)
                {
                    row.IsSelected = false;
                }
                else
                {
                    // Chỉ chọn lại nếu dòng này không bị ẩn do số lượng xuất = 0
                    if (HideZeroQuantityCheckBox.IsChecked != true || row.Quantity > 0)
                    {
                        row.IsSelected = true;
                    }
                }
            }
        }

        UpdateSummary();
    }

    private void HideZeroQuantity_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        var hide = HideZeroQuantityCheckBox.IsChecked == true;
        UiPreferenceService.SaveHideZeroQuantityBalanceExport(hide);

        if (hide)
        {
            foreach (var row in _allRows)
            {
                if (row.Quantity <= 0)
                {
                    row.IsSelected = false;
                }
            }
        }

        _rowsView.Refresh();
        UpdateSummary();
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _rowsView)
        {
            if (item is BalanceExportItemRow row)
            {
                row.IsSelected = true;
            }
        }
    }

    private void DeselectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in _rowsView)
        {
            if (item is BalanceExportItemRow row)
            {
                row.IsSelected = false;
            }
        }
    }

    private void HeaderSelectAll_OnChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        foreach (var item in _rowsView)
        {
            if (item is BalanceExportItemRow row)
            {
                row.IsSelected = true;
            }
        }
    }

    private void HeaderSelectAll_OnUnchecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionSync)
        {
            return;
        }

        foreach (var item in _rowsView)
        {
            if (item is BalanceExportItemRow row)
            {
                row.IsSelected = false;
            }
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _rowsView.Refresh();
    }

    private void ExportWord_OnClick(object sender, RoutedEventArgs e)
    {
        ConfirmExport(BalanceExportFormat.Word);
    }

    private void ExportExcel_OnClick(object sender, RoutedEventArgs e)
    {
        ConfirmExport(BalanceExportFormat.Excel);
    }

    private void ConfirmExport(BalanceExportFormat format)
    {
        var selectedIds = _allRows.Where(x => x.IsSelected).Select(x => x.LineId).ToHashSet();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show(
                this,
                "Vui lòng tick chọn ít nhất một mặt hàng để xuất sổ cân đối.",
                "Chưa chọn mặt hàng",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Result = new BalanceExportResult(
            format,
            selectedIds,
            OmitZeroStockCheckBox.IsChecked == true,
            HideZeroQuantityCheckBox.IsChecked == true);

        DialogResult = true;
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
