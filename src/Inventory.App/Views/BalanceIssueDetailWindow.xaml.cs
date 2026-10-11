using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Inventory.App.Helpers;
using Inventory.Data.Services;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Microsoft.Win32;

namespace Inventory.App.Views;

public partial class BalanceIssueDetailWindow : Window
{
    private readonly WordExportService _wordExport;
    private readonly InventoryService _inventoryService;
    private readonly IReadOnlyDictionary<int, Item> _itemsById;
    private readonly string _issueTemplatePath;
    private readonly string _balanceTemplatePath;
    private readonly IReadOnlyList<SubordinateUnit> _subordinateUnits;
    private bool _syncingOverviewSelection;
    private bool _suppressClosePrompt;
    private bool _showUnitCodes;

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

    public BalanceIssueDetailViewModel ViewModel { get; }

    public BalanceIssueDetailWindow(
        WordExportService wordExport,
        InventoryService inventoryService,
        Issue balance,
        IReadOnlyList<Issue> unitIssues,
        IReadOnlyDictionary<int, Item> itemsById,
        string issueTemplatePath,
        string balanceTemplatePath,
        IReadOnlyDictionary<int, BalanceStockSnapshot> stockSnapshots,
        IReadOnlyList<SubordinateUnit> subordinateUnits)
    {
        _wordExport = wordExport;
        _inventoryService = inventoryService;
        _itemsById = itemsById;
        _issueTemplatePath = issueTemplatePath;
        _balanceTemplatePath = balanceTemplatePath;
        ViewModel = new BalanceIssueDetailViewModel(
            balance,
            unitIssues,
            itemsById,
            stockSnapshots,
            subordinateUnits);
        _subordinateUnits = ViewModel.OverviewUnits;
        _showUnitCodes = UiPreferenceService.LoadShowBalanceUnitCodes();

        InitializeComponent();
        DataContext = ViewModel;
        Title = $"Chi tiết phiếu cân đối {balance.IssueNumber}";
        UpdateOverviewUnitHeaderModeButton();
        BuildOverviewUnitColumns(_subordinateUnits);
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt || !ViewModel.HasDirtyRows)
        {
            return;
        }

        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var discard = MessageBox.Show(
            this,
            "Phiếu xuất theo đơn vị có thay đổi chưa lưu.\n\nBỏ các thay đổi và đóng cửa sổ?",
            "Thay đổi chưa lưu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Cancel = discard != MessageBoxResult.Yes;
        _suppressClosePrompt = !e.Cancel;
    }

    private void BuildOverviewUnitColumns(IReadOnlyList<SubordinateUnit> subordinateUnits)
    {
        OverviewUnitsGrid.Columns.Clear();

        if (subordinateUnits.Count == 0 || !ViewModel.HasUnitAllocations)
        {
            OverviewUnitsPanel.Visibility = Visibility.Collapsed;
            OverviewSplitter.Visibility = Visibility.Collapsed;
            OverviewSplitHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            OverviewSplitHost.ColumnDefinitions[2].Width = new GridLength(0);
            return;
        }

        var centerStyle = (Style)FindResource("CenterAlignedCell");
        for (var i = 0; i < subordinateUnits.Count; i++)
        {
            var unit = subordinateUnits[i];
            var column = new DataGridTextColumn
            {
                Header = GetUnitHeader(unit),
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
            OverviewUnitsGrid.Columns.Add(column);
        }

        DataGridScrollSync.Attach(OverviewGrid, OverviewUnitsGrid);
    }

    private string GetUnitHeader(SubordinateUnit unit) =>
        _showUnitCodes && !string.IsNullOrWhiteSpace(unit.UnitCode)
            ? unit.UnitCode
            : unit.Name;

    private void ToggleOverviewUnitHeaderMode_OnClick(object sender, RoutedEventArgs e)
    {
        _showUnitCodes = !_showUnitCodes;
        UiPreferenceService.SaveShowBalanceUnitCodes(_showUnitCodes);
        UpdateOverviewUnitHeaderModeButton();

        for (var i = 0; i < OverviewUnitsGrid.Columns.Count && i < _subordinateUnits.Count; i++)
        {
            OverviewUnitsGrid.Columns[i].Header = GetUnitHeader(_subordinateUnits[i]);
        }
    }

    private void UpdateOverviewUnitHeaderModeButton()
    {
        ToggleOverviewUnitHeaderModeButton.Content =
            _showUnitCodes ? "Hiện tên đơn vị" : "Hiện mã đơn vị";
    }

    private void OverviewGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOverviewSelection)
        {
            return;
        }

        _syncingOverviewSelection = true;
        DataGridScrollSync.SyncFullRowSelection(OverviewGrid, OverviewUnitsGrid);
        _syncingOverviewSelection = false;
    }

    private void OverviewUnitsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOverviewSelection)
        {
            return;
        }

        _syncingOverviewSelection = true;
        DataGridScrollSync.SyncFullRowSelection(OverviewUnitsGrid, OverviewGrid);
        _syncingOverviewSelection = false;
    }

    private void SelectAllUnits_OnClick(object sender, RoutedEventArgs e) => ViewModel.SetAllSelected(true);

    private void ClearUnitSelection_OnClick(object sender, RoutedEventArgs e) => ViewModel.SetAllSelected(false);

    private async void SaveUnitHeaders_OnClick(object sender, RoutedEventArgs e)
    {
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        try
        {
            var patches = ViewModel.BuildHeaderPatches();
            await _inventoryService.UpdateUnitIssueHeadersAsync(ViewModel.Balance.Id, patches);
            ViewModel.ClearDirtyFlags();
            MessageBox.Show(
                this,
                "Đã lưu số phiếu và thông tin các phiếu xuất con.",
                "Thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không lưu được", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UnitIssuesGrid_OnPreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var cell = FindAncestor<DataGridCell>(source);
        if (cell?.Column is null)
        {
            return;
        }

        // Xem trước chỉ thực hiện qua nút riêng. Nháy đúp ô sửa được luôn
        // chuyển ô sang chế độ nhập, kể cả khi SelectionUnit đang chọn cả dòng.
        e.Handled = true;
        if (cell.Column.IsReadOnly || cell.DataContext is not UnitIssueListRow row)
        {
            return;
        }

        UnitIssuesGrid.SelectedItem = row;
        UnitIssuesGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
        cell.Focus();
        UnitIssuesGrid.BeginEdit();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void PreviewUnitIssueRow_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: UnitIssueListRow row })
        {
            return;
        }

        ViewModel.SelectedUnitIssue = row;
        UnitIssuesGrid.SelectedItem = row;
        OpenUnitPreview(row.Issue);
    }

    private void ViewUnitIssue_OnClick(object sender, RoutedEventArgs e)
    {
        var selectedRow = GetCurrentUnitIssueRow();
        if (selectedRow is null)
        {
            MessageBox.Show(this, "Vui lòng chọn một phiếu xuất (click dòng).", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenUnitPreview(selectedRow.Issue);
    }

    private UnitIssueListRow? GetCurrentUnitIssueRow()
    {
        var row = UnitIssuesGrid.SelectedItem as UnitIssueListRow
            ?? UnitIssuesGrid.CurrentItem as UnitIssueListRow
            ?? ViewModel.SelectedUnitIssue;
        if (row is not null)
        {
            ViewModel.SelectedUnitIssue = row;
        }

        return row;
    }

    private void OpenUnitPreview(Issue unitIssue)
    {
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var window = new VoucherPreviewWindow(
            _wordExport,
            unitIssue,
            _itemsById,
            _issueTemplatePath,
            [],
            showWordExportButton: false)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private Task EnsureSavedBeforeExportAsync()
    {
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        UnitIssuesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!ViewModel.HasDirtyRows)
        {
            return Task.CompletedTask;
        }

        MessageBox.Show(
            this,
            "Có thay đổi chưa lưu trên phiếu xuất.\nHãy bấm “Lưu thay đổi” trước khi xuất Word.",
            "Thay đổi chưa lưu",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        throw new OperationCanceledException();
    }

    private void ExportSelectedUnitIssues_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.GetSelectedIssues().Count == 0)
        {
            MessageBox.Show(
                this,
                "Hãy tick ít nhất một phiếu xuất.",
                "Thông báo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var choice = MessageBox.Show(
            this,
            "Chọn cách xuất phiếu đã tick:\n\n" +
            "• Yes (Có): Gộp vào một file Word\n" +
            "• No (Không): Tách thành từng file Word\n" +
            "• Cancel (Hủy): Không xuất",
            "Xuất phiếu xuất đã chọn",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            ExportCombined_OnClick(sender, e);
        }
        else if (choice == MessageBoxResult.No)
        {
            ExportSeparate_OnClick(sender, e);
        }
    }

    private async void ExportCombined_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.GetSelectedIssues();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Hãy tick ít nhất một phiếu xuất để xuất gộp.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureIssueTemplate())
        {
            return;
        }

        try
        {
            await EnsureSavedBeforeExportAsync();

            var dialog = new SaveFileDialog
            {
                Filter = "Word (*.docx)|*.docx",
                FileName = selected.Count == 1
                    ? $"PX_{FileNameSanitizer.Sanitize(selected[0].IssueNumber)}.docx"
                    : $"PX_{FileNameSanitizer.Sanitize(ViewModel.Balance.IssueNumber)}_Gop_{selected.Count}phieu.docx"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            await RunBusyAsync(
                $"Đang xuất gộp {selected.Count} phiếu Word...",
                () => _wordExport.ExportIssuesCombined(_issueTemplatePath, dialog.FileName, selected, _itemsById));
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
            // user cancelled save prompt
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không xuất được Word", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportSeparate_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = ViewModel.GetSelectedIssues();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Hãy tick ít nhất một phiếu xuất để xuất từng file.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureIssueTemplate())
        {
            return;
        }

        try
        {
            await EnsureSavedBeforeExportAsync();

            var folderDialog = new OpenFolderDialog { Title = "Chọn thư mục lưu phiếu xuất" };
            if (folderDialog.ShowDialog(this) != true)
            {
                return;
            }

            await RunBusyAsync(
                $"Đang xuất {selected.Count} phiếu Word...",
                () =>
                {
                    foreach (var issue in selected)
                    {
                        var output = Path.Combine(
                            folderDialog.FolderName,
                            $"PX_{FileNameSanitizer.Sanitize(issue.IssueNumber)}_{FileNameSanitizer.Sanitize(issue.ReceivedUnit)}.docx");
                        _wordExport.ExportIssue(_issueTemplatePath, output, issue, _itemsById);
                    }
                });

            Process.Start(new ProcessStartInfo(folderDialog.FolderName) { UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không xuất được Word", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportOverview_OnClick(object sender, RoutedEventArgs e)
    {
        await ExportOverviewCoreAsync(BalanceExportFormat.Word);
    }

    private async void ExportOverviewExcel_OnClick(object sender, RoutedEventArgs e)
    {
        await ExportOverviewCoreAsync(BalanceExportFormat.Excel);
    }

    private async Task ExportOverviewCoreAsync(BalanceExportFormat preferredFormat)
    {
        try
        {
            var stockSnapshots = await _inventoryService.GetBalanceStockSnapshotsAsync(
                ViewModel.Balance.Id);

            var exportDialog = new BalanceExportDialogWindow(
                ViewModel.Balance,
                _itemsById,
                stockSnapshots)
            {
                Owner = this
            };

            if (exportDialog.ShowDialog() != true || exportDialog.Result is null)
            {
                return;
            }

            var exportResult = exportDialog.Result;
            if (exportResult.Format == BalanceExportFormat.Word)
            {
                if (!EnsureBalanceTemplate())
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Filter = "Word (*.docx)|*.docx",
                    FileName = $"SoCanDoi_{FileNameSanitizer.Sanitize(ViewModel.Balance.IssueNumber)}.docx"
                };
                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                await RunBusyAsync(
                    "Đang xuất Word sổ cân đối...",
                    () => _wordExport.ExportBalanceOverview(
                        _balanceTemplatePath,
                        dialog.FileName,
                        ViewModel.Balance,
                        _itemsById,
                        ViewModel.OverviewUnits,
                        stockSnapshots,
                        omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                        selectedLineIds: exportResult.SelectedLineIds));
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            else
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Excel (*.xlsx)|*.xlsx",
                    FileName = $"SoCanDoi_{FileNameSanitizer.Sanitize(ViewModel.Balance.IssueNumber)}.xlsx"
                };
                if (dialog.ShowDialog(this) != true)
                {
                    return;
                }

                var unitIssues = await _inventoryService.GetGeneratedUnitIssuesAsync(
                    ViewModel.Balance.Id);
                await RunBusyAsync(
                    "Đang xuất Excel sổ cân đối...",
                    () => new InventoryExcelExportService().ExportBalance(
                        dialog.FileName,
                        ViewModel.Balance,
                        _itemsById,
                        ViewModel.OverviewUnits,
                        stockSnapshots,
                        unitIssues,
                        omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                        selectedLineIds: exportResult.SelectedLineIds));
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Không xuất được sổ cân đối", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool EnsureIssueTemplate()
    {
        if (!string.IsNullOrWhiteSpace(_issueTemplatePath) && File.Exists(_issueTemplatePath))
        {
            return true;
        }

        MessageBox.Show(this, "Chưa cấu hình template Word phiếu xuất trong Cài đặt.", "Thiếu template", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private bool EnsureBalanceTemplate()
    {
        if (!string.IsNullOrWhiteSpace(_balanceTemplatePath) && File.Exists(_balanceTemplatePath))
        {
            return true;
        }

        MessageBox.Show(this, "Chưa cấu hình template Word phiếu cân đối trong Cài đặt.", "Thiếu template", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}

public sealed partial class BalanceIssueDetailViewModel : ObservableObject
{
    public Issue Balance { get; }
    public string TitleText { get; }
    public string StockSnapshotCaption { get; }
    public bool HasUnitAllocations { get; }
    public IReadOnlyList<SubordinateUnit> OverviewUnits { get; }
    public ObservableCollection<UnitIssueListRow> UnitIssueRows { get; } = [];
    public ObservableCollection<VoucherLinePreviewRow> OverviewRows { get; } = [];

    [ObservableProperty] private UnitIssueListRow? selectedUnitIssue;

    public string SelectedUnitCountText =>
        $"Đã tick: {UnitIssueRows.Count(x => x.IsSelected)}/{UnitIssueRows.Count}";

    public bool HasDirtyRows => UnitIssueRows.Any(x => x.IsDirty);
    public bool HasUnsavedChanges => HasDirtyRows;

    [ObservableProperty] private bool omitZeroStockAtIssueDate = UiPreferenceService.LoadOmitZeroStockAtIssueDate();

    partial void OnOmitZeroStockAtIssueDateChanged(bool value)
    {
        UiPreferenceService.SaveOmitZeroStockAtIssueDate(value);
    }

    public BalanceIssueDetailViewModel(
        Issue balance,
        IReadOnlyList<Issue> unitIssues,
        IReadOnlyDictionary<int, Item> itemsById,
        IReadOnlyDictionary<int, BalanceStockSnapshot> stockSnapshots,
        IReadOnlyList<SubordinateUnit> subordinateUnits)
    {
        Balance = balance;
        TitleText = $"Phiếu cân đối {balance.IssueNumber}";
        StockSnapshotCaption =
            $"Tồn ngày lập phiếu − Số lượng xuất − Còn lại (ngày {balance.IssueDate:dd/MM/yyyy})";
        HasUnitAllocations = balance.Lines.Any(l => l.Allocations.Count > 0);
        OverviewUnits = subordinateUnits.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();

        var index = 1;
        foreach (var issue in unitIssues.OrderBy(x => x.IssueNumber))
        {
            var row = new UnitIssueListRow(issue)
            {
                RowNumber = index++,
                IsSelected = true
            };
            row.PropertyChanged += OnUnitRowPropertyChanged;
            UnitIssueRows.Add(row);
        }

        var rowIndex = 1;
        foreach (var line in balance.Lines.OrderBy(x => x.Id))
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            stockSnapshots.TryGetValue(line.ItemId, out var stockSnapshot);
            var unitQtys = OverviewUnits
                .Select(u =>
                {
                    var alloc = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == u.Id);
                    return alloc is { Quantity: > 0 } ? alloc.Quantity : (decimal?)null;
                })
                .ToList();

            OverviewRows.Add(new VoucherLinePreviewRow
            {
                RowNumber = rowIndex++,
                ItemCode = item?.ItemCode ?? $"#{line.ItemId}",
                ItemName = item?.ItemName ?? string.Empty,
                Quantity = line.Quantity,
                TotalAtIssueDate = stockSnapshot?.TotalQuantity ?? 0,
                RemainingAtIssueDate = stockSnapshot?.RemainingQuantity ?? 0,
                UnitPrice = line.UnitPrice,
                Amount = line.Amount,
                Note = line.Note,
                UnitQuantities = unitQtys
            });
        }
    }

    public void SetAllSelected(bool selected)
    {
        foreach (var row in UnitIssueRows)
        {
            row.IsSelected = selected;
        }
    }

    public IReadOnlyList<Issue> GetSelectedIssues() =>
        UnitIssueRows.Where(x => x.IsSelected).Select(x => x.Issue).ToList();

    public IReadOnlyList<UnitIssueHeaderPatch> BuildHeaderPatches() =>
        UnitIssueRows.Select(r => new UnitIssueHeaderPatch
        {
            IssueId = r.Issue.Id,
            IssueNumber = r.IssueNumber,
            IssueDate = r.IssueDate,
            ReceivedBy = r.ReceivedBy,
            DeliveredBy = r.DeliveredBy,
            Reason = r.Reason
        }).ToList();

    public void ClearDirtyFlags()
    {
        foreach (var row in UnitIssueRows)
        {
            row.ClearDirty();
        }

        OnPropertyChanged(nameof(HasDirtyRows));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private void OnUnitRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnitIssueListRow.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedUnitCountText));
        }
        else if (e.PropertyName == nameof(UnitIssueListRow.IsDirty))
        {
            OnPropertyChanged(nameof(HasDirtyRows));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
    }
}

public sealed class UnitIssueListRow : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isDirty;
    private string _issueNumber;
    private string _receivedBy;
    private string _deliveredBy;
    private string _reason;
    private DateTime _issueDate;

    public UnitIssueListRow(Issue issue)
    {
        Issue = issue;
        UnitName = issue.ReceivedUnit;
        LineCount = issue.Lines.Count;
        TotalQuantity = issue.Lines.Sum(x => x.Quantity);
        TotalAmount = issue.TotalAmount;
        _issueNumber = issue.IssueNumber;
        _receivedBy = issue.ReceivedBy;
        _deliveredBy = issue.DeliveredBy;
        _reason = issue.Reason;
        _issueDate = issue.IssueDate.Date;
    }

    public int RowNumber { get; init; }
    public Issue Issue { get; }
    public string UnitName { get; }
    public int LineCount { get; }
    public decimal TotalQuantity { get; }
    public decimal TotalAmount { get; }
    public bool IsDirty => _isDirty;

    public string IssueNumber
    {
        get => _issueNumber;
        set
        {
            var next = value ?? string.Empty;
            if (_issueNumber == next)
            {
                return;
            }

            _issueNumber = next;
            Issue.IssueNumber = next;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    public string ReceivedBy
    {
        get => _receivedBy;
        set
        {
            var next = value ?? string.Empty;
            if (_receivedBy == next)
            {
                return;
            }

            _receivedBy = next;
            Issue.ReceivedBy = next;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    public string DeliveredBy
    {
        get => _deliveredBy;
        set
        {
            var next = value ?? string.Empty;
            if (_deliveredBy == next)
            {
                return;
            }

            _deliveredBy = next;
            Issue.DeliveredBy = next;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    public string Reason
    {
        get => _reason;
        set
        {
            var next = value ?? string.Empty;
            if (_reason == next)
            {
                return;
            }

            _reason = next;
            Issue.Reason = next;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    public DateTime IssueDate
    {
        get => _issueDate;
        set
        {
            var next = value.Date;
            if (_issueDate == next)
            {
                return;
            }

            _issueDate = next;
            Issue.IssueDate = next;
            MarkDirty();
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void ClearDirty()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    private void MarkDirty()
    {
        if (_isDirty)
        {
            return;
        }

        _isDirty = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
