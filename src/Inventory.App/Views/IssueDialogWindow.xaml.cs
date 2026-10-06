using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Inventory.App.Converters;
using Inventory.App.Helpers;
using Inventory.Reporting.Services;

namespace Inventory.App.Views;

public partial class IssueDialogWindow : Window
{
    private static readonly SolidColorBrush HeaderHighlightBrush = CreateFreezeBrush(0xFF, 0x93, 0xC5, 0xFD);
    private static readonly SolidColorBrush HeaderNormalBrush = CreateFreezeBrush(0xFF, 0xF1, 0xF5, 0xF9);
    private const double PreferredUnitColumnWidth = 82;
    private const double MinimumUnitColumnWidth = 58;
    private const double MaximumUnitColumnWidth = 110;
    private const double PreferredMainPaneWidth = 700;
    private const double WindowHorizontalPadding = 30;

    private readonly IssueDialogViewModel _viewModel;
    private readonly IssueExcelWorkspaceService _excelWorkspaceService = new();
    private static readonly NullableDecimalConverter NullableDecimalConverter = new();
    private bool _syncingSelection;
    private bool _suppressClosePrompt;
    private bool _showUnitCodes;
    private DataGridColumnHeader? _highlightedUnitHeader;
    private string? _excelWorkspacePath;
    private FileSystemWatcher? _excelWorkspaceWatcher;
    private DispatcherTimer? _excelSyncDebounceTimer;
    private bool _excelSyncInProgress;

    /// <summary>Lưu xuống DB trước khi đóng cửa sổ. Lỗi → giữ popup, chỉ báo thông báo.</summary>
    public Func<Task>? PersistAsync { get; set; }

    public IssueDialogWindow(IssueDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _showUnitCodes = UiPreferenceService.LoadShowBalanceUnitCodes();
        DataContext = _viewModel;
        UpdateUnitHeaderModeButton();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private static SolidColorBrush CreateFreezeBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt || !_viewModel.IsDirty)
        {
            StopExcelWorkspace(deleteFile: true);
            return;
        }

        var discard = MessageBox.Show(
            this,
            "Phiếu cân đối có thay đổi chưa lưu.\n\nBỏ các thay đổi và đóng cửa sổ?",
            "Thay đổi chưa lưu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Cancel = discard != MessageBoxResult.Yes;
        _suppressClosePrompt = !e.Cancel;
        if (!e.Cancel)
        {
            StopExcelWorkspace(deleteFile: true);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InsertUnitColumns();
        if (_viewModel.HasSubordinateUnits)
        {
            QuantityColumn.IsReadOnly = true;
            QuantityColumn.Header = "Số lượng (tổng)";
            FitWindowAndUnitColumns();
            DataGridScrollSync.Attach(LinesGrid, UnitsGrid);
            LinesGrid.CurrentCellChanged += SplitGrids_OnCurrentCellChanged;
            UnitsGrid.CurrentCellChanged += SplitGrids_OnCurrentCellChanged;
        }
        else
        {
            CollapseUnitsPanel();
        }
    }

    /// <summary>
    /// Mở rộng cửa sổ theo số đơn vị, nhưng không vượt vùng làm việc của màn hình.
    /// Khi hết chỗ, các cột co đến mức vẫn đọc được mã đơn vị trước khi phải cuộn ngang.
    /// </summary>
    private void FitWindowAndUnitColumns()
    {
        var count = _viewModel.SubordinateUnits.Count;
        if (count == 0)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var desiredWidth = PreferredMainPaneWidth
                           + LinesSplitter.Width
                           + WindowHorizontalPadding
                           + count * PreferredUnitColumnWidth;
        var maximumWidth = Math.Max(MinWidth, workArea.Width - 24);
        var targetWidth = Math.Min(desiredWidth, maximumWidth);

        if (WindowState == WindowState.Normal && targetWidth > ActualWidth)
        {
            Width = targetWidth;
            Left = Math.Clamp(Left, workArea.Left, workArea.Right - targetWidth);
        }

        Dispatcher.BeginInvoke(ResizeUnitColumnsToViewport, DispatcherPriority.Loaded);
    }

    private void LinesSplitHost_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ResizeUnitColumnsToViewport();

    private void ResizeUnitColumnsToViewport()
    {
        ResizeMainColumnsToViewport();

        if (UnitsGrid.Columns.Count == 0 || UnitsGrid.ActualWidth <= 0)
        {
            return;
        }

        // Chừa bề rộng tối thiểu cho scrollbar và đường viền.
        var usableWidth = Math.Max(0, UnitsGrid.ActualWidth - 18);
        var width = Math.Clamp(
            usableWidth / UnitsGrid.Columns.Count,
            MinimumUnitColumnWidth,
            MaximumUnitColumnWidth);

        foreach (var column in UnitsGrid.Columns)
        {
            column.MinWidth = MinimumUnitColumnWidth;
            column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }
    }

    private void ResizeMainColumnsToViewport()
    {
        if (LinesGrid.Columns.Count == 0 || LinesGrid.ActualWidth <= 0)
        {
            return;
        }

        // Ưu tiên luôn thấy các cột chính. Chỉ thu hẹp khi cửa sổ không đủ rộng,
        // và vẫn giữ đủ diện tích cho mã/tên hàng.
        var preferred = new[] { 42d, 42d, 150d, 96d, 90d, 75d, 85d, 95d, 110d };
        var minimum = new[] { 36d, 36d, 110d, 70d, 70d, 60d, 70d, 75d, 80d };
        var available = Math.Max(0, LinesGrid.ActualWidth - 18);
        var preferredTotal = preferred.Sum();
        var minimumTotal = minimum.Sum();

        for (var index = 0; index < LinesGrid.Columns.Count && index < preferred.Length; index++)
        {
            var width = available >= preferredTotal
                ? preferred[index]
                : available <= minimumTotal
                    ? minimum[index]
                    : minimum[index]
                      + (preferred[index] - minimum[index])
                      * ((available - minimumTotal) / (preferredTotal - minimumTotal));

            LinesGrid.Columns[index].MinWidth = minimum[index];
            LinesGrid.Columns[index].Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }
    }

    private void CollapseUnitsPanel()
    {
        UnitsPanel.Visibility = Visibility.Collapsed;
        LinesSplitter.Visibility = Visibility.Collapsed;
        LinesColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
        UnitsColumnDefinition.Width = new GridLength(0);
    }

    private void InsertUnitColumns()
    {
        UnitsGrid.Columns.Clear();
        if (!_viewModel.HasSubordinateUnits)
        {
            return;
        }

        var centerStyle = CreateCenterTextStyle();
        var editStyle = CreateCenterEditStyle();

        for (var i = 0; i < _viewModel.SubordinateUnits.Count; i++)
        {
            var unit = _viewModel.SubordinateUnits[i];
            var binding = new Binding($"UnitAllocations[{i}].Quantity")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Mode = BindingMode.TwoWay,
                Converter = NullableDecimalConverter,
                TargetNullValue = string.Empty
            };

            // TextBlock khi chọn ô; TextBox chỉ hiện khi gõ/F2 (điều hướng kiểu Excel).
            UnitsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = GetUnitHeader(unit.Name, unit.UnitCode),
                Binding = binding,
                Width = new DataGridLength(PreferredUnitColumnWidth),
                MinWidth = MinimumUnitColumnWidth,
                ElementStyle = centerStyle,
                EditingElementStyle = editStyle
            });
        }
    }

    private static Style CreateCenterTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style CreateCenterEditStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        return style;
    }

    private string GetUnitHeader(string unitName, string unitCode) =>
        _showUnitCodes && !string.IsNullOrWhiteSpace(unitCode) ? unitCode : unitName;

    private void ToggleUnitHeaderMode_OnClick(object sender, RoutedEventArgs e)
    {
        _showUnitCodes = !_showUnitCodes;
        UiPreferenceService.SaveShowBalanceUnitCodes(_showUnitCodes);
        UpdateUnitHeaderModeButton();
        RefreshUnitColumnHeaders();
    }

    private void RefreshUnitColumnHeaders()
    {
        for (var i = 0; i < UnitsGrid.Columns.Count && i < _viewModel.SubordinateUnits.Count; i++)
        {
            var unit = _viewModel.SubordinateUnits[i];
            UnitsGrid.Columns[i].Header = GetUnitHeader(unit.Name, unit.UnitCode);
        }
    }

    private void UpdateUnitHeaderModeButton()
    {
        ToggleUnitHeaderModeButton.Content =
            _showUnitCodes ? "Hiện tên đơn vị" : "Hiện mã đơn vị";
    }

    private void LinesGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        SyncPartnerGrid(LinesGrid, UnitsGrid, preferItemCodeOnTarget: false);
        _syncingSelection = false;
        Dispatcher.BeginInvoke(UpdateFocusHighlights, DispatcherPriority.Input);
    }

    private void UnitsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        SyncPartnerGrid(UnitsGrid, LinesGrid, preferItemCodeOnTarget: true);
        _syncingSelection = false;
        Dispatcher.BeginInvoke(UpdateFocusHighlights, DispatcherPriority.Input);
    }

    private void SplitGrids_OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        _syncingSelection = true;
        if (ReferenceEquals(sender, UnitsGrid))
        {
            SyncPartnerGrid(UnitsGrid, LinesGrid, preferItemCodeOnTarget: true);
        }
        else if (ReferenceEquals(sender, LinesGrid))
        {
            SyncPartnerGrid(LinesGrid, UnitsGrid, preferItemCodeOnTarget: false);
        }

        _syncingSelection = false;
        UpdateFocusHighlights();
    }

    private void SyncPartnerGrid(DataGrid source, DataGrid target, bool preferItemCodeOnTarget)
    {
        var item = source.CurrentCell.IsValid ? source.CurrentCell.Item : source.SelectedItem;
        if (item is null || target.Columns.Count == 0)
        {
            return;
        }

        target.SelectedItem = item;

        DataGridColumn? column = null;
        if (preferItemCodeOnTarget)
        {
            column = ItemCodeColumn;
        }
        else if (target.CurrentCell.IsValid && ReferenceEquals(target.CurrentCell.Item, item))
        {
            column = target.CurrentCell.Column;
        }

        column ??= target.Columns.OrderBy(c => c.DisplayIndex).FirstOrDefault(c => !c.IsReadOnly)
            ?? target.Columns.OrderBy(c => c.DisplayIndex).First();

        var cell = new DataGridCellInfo(item, column);
        target.CurrentCell = cell;
        target.SelectedCells.Clear();
        target.SelectedCells.Add(cell);
        target.ScrollIntoView(item, column);
        source.ScrollIntoView(item);
    }

    private void UpdateFocusHighlights()
    {
        ClearUnitHeaderHighlight();

        var activeItem = UnitsGrid.CurrentCell.IsValid
            ? UnitsGrid.CurrentCell.Item
            : LinesGrid.CurrentCell.IsValid
                ? LinesGrid.CurrentCell.Item
                : null;

        // Tô sáng tiêu đề cột đơn vị đang chọn trên lưới phải.
        var unitColumn = UnitsGrid.CurrentCell.Column ?? UnitsGrid.CurrentColumn;
        if (unitColumn is not null)
        {
            HighlightUnitHeader(unitColumn);
        }

        // Khi đang làm việc bên phải: bên trái chỉ tô ô mã hàng của dòng đó.
        if (activeItem is null || ItemCodeColumn is null || !UnitsGrid.IsKeyboardFocusWithin)
        {
            return;
        }

        var itemCell = new DataGridCellInfo(activeItem, ItemCodeColumn);
        LinesGrid.SelectedCells.Clear();
        LinesGrid.SelectedCells.Add(itemCell);
        if (!ReferenceEquals(LinesGrid.CurrentCell.Item, activeItem)
            || !ReferenceEquals(LinesGrid.CurrentCell.Column, ItemCodeColumn))
        {
            LinesGrid.CurrentCell = itemCell;
        }
    }

    private void HighlightUnitHeader(DataGridColumn column)
    {
        var header = FindColumnHeader(UnitsGrid, column);
        if (header is null)
        {
            return;
        }

        header.Background = HeaderHighlightBrush;
        _highlightedUnitHeader = header;
    }

    private void ClearUnitHeaderHighlight()
    {
        if (_highlightedUnitHeader is not null)
        {
            _highlightedUnitHeader.Background = HeaderNormalBrush;
            _highlightedUnitHeader = null;
        }
    }

    private static DataGridColumnHeader? FindColumnHeader(DataGrid grid, DataGridColumn column)
    {
        var presenter = FindVisualChild<DataGridColumnHeadersPresenter>(grid);
        if (presenter is null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(presenter); i++)
        {
            if (VisualTreeHelper.GetChild(presenter, i) is DataGridColumnHeader header
                && ReferenceEquals(header.Column, column))
            {
                return header;
            }
        }

        // Một số theme bọc header trong container trung gian.
        return FindHeaderRecursive(presenter, column);
    }

    private static DataGridColumnHeader? FindHeaderRecursive(DependencyObject parent, DataGridColumn column)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is DataGridColumnHeader header && ReferenceEquals(header.Column, column))
            {
                return header;
            }

            var nested = FindHeaderRecursive(child, column);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private async void OpenItemPicker_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _viewModel.SelectableItems)
        {
            item.IsSelected = false;
        }

        foreach (var receipt in _viewModel.ReceiptSuggestions)
        {
            receipt.IsSelected = false;
        }

        var pickerVm = new ItemPickerViewModel(
            _viewModel.SelectableItems,
            _viewModel.ReceiptSuggestions,
            _viewModel.Items.ToDictionary(x => x.Id),
            _viewModel.Lines.Select(x => x.ItemCode),
            _viewModel.IssueDate,
            async receiptId => await _viewModel.ResolveReceiptAsync(receiptId),
            async (code, date) => await _viewModel.ResolveAvailabilityAsync(code, date));

        var picker = new ItemPickerWindow(pickerVm) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedItems.Count == 0)
        {
            return;
        }

        await _viewModel.AddItemsFromSelectionAsync(picker.SelectedItems);
    }

    private void SelectAllLines_OnChanged(object sender, RoutedEventArgs e)
    {
        var isSelected = sender is CheckBox { IsChecked: true };
        foreach (var line in _viewModel.Lines)
        {
            line.IsSelected = isSelected;
        }
    }

    private void Save_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CommitPendingLineEdits();
    }

    private void CommitPendingLineEdits()
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        UnitsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        UnitsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (System.Windows.Input.Keyboard.FocusedElement is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingLineEdits();

        if (!_viewModel.Validate(out var message))
        {
            MessageBox.Show(this, message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PersistAsync is not null)
        {
            try
            {
                BusyOverlay.Message = "Đang lưu phiếu cân đối...";
                BusyOverlay.Visibility = Visibility.Visible;
                await Task.Yield();
                await PersistAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ExceptionMessageFormatter.Format(ex),
                    "Không lưu được phiếu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            finally
            {
                BusyOverlay.Visibility = Visibility.Collapsed;
            }
        }

        _viewModel.AcceptChanges();
        _suppressClosePrompt = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void ExcelWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_excelWorkspaceWatcher is not null)
        {
            // Đang theo dõi: bấm lần nữa để dừng đồng bộ.
            StopExcelWorkspace(deleteFile: false);
            return;
        }

        if (!_viewModel.IsModeB)
        {
            if (_viewModel.Lines.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "Hãy thêm mặt hàng trên form trước.\nFile Excel chỉ dùng để nhập số lượng theo đơn vị, không thêm mặt hàng mới.",
                    "Chưa có dòng hàng",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (!_viewModel.HasSubordinateUnits)
            {
                MessageBox.Show(
                    this,
                    "Chức năng này chỉ dùng khi có đơn vị trực thuộc để nhập số lượng theo từng đơn vị.",
                    "Không có đơn vị trực thuộc",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        ExcelWorkspaceButton.IsEnabled = false;
        ShowBusy("Đang tạo file Excel...");
        try
        {
            var lines = _viewModel.Lines
                .Select(line => new IssueExcelLine(
                    line.Category,
                    line.ItemName,
                    line.StockAtIssueDateQuantity,
                    line.UnitPrice,
                    line.Note,
                    line.Quantity,
                    line.GetAllocationsDictionary()))
                .ToList();
            var units = _viewModel.SubordinateUnits.ToList();

            var fileName = $"PhieuCanDoi_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(Path.GetTempPath(), "InventoryManagement", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await Task.Yield();
            await Task.Run(() => _excelWorkspaceService.ExportWorkspace(filePath, lines, units, _viewModel.IsModeB));

            _excelWorkspacePath = filePath;
            ShowBusy("Đang mở Excel...");
            await Task.Yield();
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });

            StartExcelWorkspaceWatcher(filePath);
            ExcelWorkspaceButton.Content = "Dừng đồng bộ Excel";
            SetExcelSyncStatus(_viewModel.IsModeB
                ? "Chế độ B: Có thể dán/nhập MãHàng và SốLượng vào file Excel, rồi Ctrl+S để đồng bộ."
                : "Chỉ nhập số lượng theo đơn vị (ô xanh), rồi Ctrl+S để đồng bộ.");
        }
        catch (Exception ex)
        {
            StopExcelWorkspace(deleteFile: false);
            MessageBox.Show(this, ex.Message, "Không mở được Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            HideBusy();
            ExcelWorkspaceButton.IsEnabled = true;
        }
    }

    private void ShowBusy(string message)
    {
        BusyOverlay.Message = message;
        BusyOverlay.Visibility = Visibility.Visible;
    }

    private void HideBusy() => BusyOverlay.Visibility = Visibility.Collapsed;

    private void StartExcelWorkspaceWatcher(string filePath)
    {
        _excelSyncDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _excelSyncDebounceTimer.Tick += async (_, _) =>
        {
            _excelSyncDebounceTimer!.Stop();
            await SyncFromExcelWorkspaceAsync();
        };

        _excelWorkspaceWatcher = new FileSystemWatcher(Path.GetDirectoryName(filePath)!)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // Excel lưu qua file tạm rồi đổi tên, nên phải nghe cả Changed/Created/Renamed.
        FileSystemEventHandler onChanged = (_, args) => OnWorkspaceFileTouched(args.FullPath);
        _excelWorkspaceWatcher.Changed += onChanged;
        _excelWorkspaceWatcher.Created += onChanged;
        _excelWorkspaceWatcher.Renamed += (_, args) => OnWorkspaceFileTouched(args.FullPath);
    }

    private void OnWorkspaceFileTouched(string fullPath)
    {
        if (_excelWorkspacePath is null
            || !string.Equals(fullPath, _excelWorkspacePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Gom nhiều sự kiện ghi liên tiếp của Excel thành một lần đồng bộ.
        Dispatcher.BeginInvoke(() =>
        {
            _excelSyncDebounceTimer?.Stop();
            _excelSyncDebounceTimer?.Start();
        });
    }

    private async Task SyncFromExcelWorkspaceAsync()
    {
        if (_excelSyncInProgress || _excelWorkspacePath is null || !File.Exists(_excelWorkspacePath))
        {
            return;
        }

        _excelSyncInProgress = true;
        try
        {
            var result = await ReadWorkspaceWithRetryAsync(_excelWorkspacePath);
            if (result is null)
            {
                SetExcelSyncStatus("Không đọc được file (Excel đang ghi). Sẽ thử lại ở lần lưu sau.");
                return;
            }

            if (!result.IsValid)
            {
                var message = string.Join(
                    Environment.NewLine,
                    result.Errors.Take(5).Select(x => x.RowNumber > 0 ? $"Dòng {x.RowNumber}: {x.Message}" : x.Message));
                SetExcelSyncStatus($"File Excel có lỗi — sửa rồi lưu lại. {message.ReplaceLineEndings(" ")}");
                return;
            }

            if (_viewModel.IsModeB)
            {
                var (updated, consolidated, unknownCodes) = await _viewModel.ApplyModeBImportAsync(result.Data);
                var statusMsg = $"Đã cập nhật {updated} mặt hàng từ Excel";
                if (consolidated > 0)
                {
                    statusMsg += $" (gộp {consolidated} dòng trùng mã)";
                }
                if (unknownCodes.Count > 0)
                {
                    statusMsg += $"; bỏ qua {unknownCodes.Count} mã không có trong danh mục: {string.Join(", ", unknownCodes)}";
                }
                SetExcelSyncStatus($"{statusMsg} lúc {DateTime.Now:HH:mm:ss}.");
            }
            else
            {
                var (updated, skipped) = await _viewModel.ApplyUnitAllocationsFromImportAsync(result.Data);
                SetExcelSyncStatus(
                    skipped > 0
                        ? $"Đã cập nhật {updated} dòng lúc {DateTime.Now:HH:mm:ss}; bỏ qua {skipped} mặt hàng không thuộc phiếu."
                        : $"Đã cập nhật số lượng {updated} dòng lúc {DateTime.Now:HH:mm:ss}.");
            }
        }
        catch (Exception ex)
        {
            SetExcelSyncStatus($"Lỗi đồng bộ: {ex.Message}");
        }
        finally
        {
            _excelSyncInProgress = false;
        }
    }

    private async Task<Inventory.Domain.Models.ImportResult<Inventory.Domain.Models.TransactionLineInput>?>
        ReadWorkspaceWithRetryAsync(string filePath)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var units = _viewModel.SubordinateUnits.ToList();
                return await Task.Run(() => _excelWorkspaceService.ImportWorkspace(filePath, units));
            }
            catch (IOException)
            {
                await Task.Delay(300 * (attempt + 1));
            }
        }

        return null;
    }

    private void SetExcelSyncStatus(string message)
    {
        ExcelSyncStatusText.Text = message;
        ExcelSyncStatusText.Visibility = string.IsNullOrEmpty(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void StopExcelWorkspace(bool deleteFile)
    {
        _excelSyncDebounceTimer?.Stop();
        _excelSyncDebounceTimer = null;

        if (_excelWorkspaceWatcher is not null)
        {
            _excelWorkspaceWatcher.EnableRaisingEvents = false;
            _excelWorkspaceWatcher.Dispose();
            _excelWorkspaceWatcher = null;
        }

        if (deleteFile && _excelWorkspacePath is not null)
        {
            try
            {
                File.Delete(_excelWorkspacePath);
            }
            catch (IOException)
            {
                // Excel vẫn giữ file — bỏ qua, file nằm trong thư mục temp.
            }
        }

        _excelWorkspacePath = null;
        if (ExcelWorkspaceButton is not null)
        {
            ExcelWorkspaceButton.Content = "Nhập bằng Excel";
        }

        SetExcelSyncStatus(string.Empty);
    }
}
