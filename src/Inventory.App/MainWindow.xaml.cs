using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using Inventory.App.Helpers;
using Inventory.App.Views;
using Inventory.Domain.Models;

namespace Inventory.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _suppressClosePrompt;
    private Point _subordinateDragStart;
    private SubordinateUnit? _subordinateDragSource;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        AppIconHelper.Apply(this);
        _viewModel = viewModel;
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_suppressClosePrompt)
        {
            return;
        }

        if (_viewModel.HasUnsavedMainEdits)
        {
            var parts = new List<string>();
            if (_viewModel.HasUnsavedDashboardMetadata) parts.Add("Dashboard");
            if (_viewModel.HasUnsavedOpeningBalances) parts.Add("tồn đầu năm");
            if (_viewModel.HasUnsavedSubordinateUnits) parts.Add("đơn vị trực thuộc");
            if (_viewModel.HasUnsavedManagedUnitIssues) parts.Add("phiếu xuất đơn vị");

            var discard = MessageBox.Show(
                this,
                $"Đang có thay đổi chưa lưu: {string.Join(", ", parts)}.\n\nBỏ các thay đổi và thoát ứng dụng?",
                "Thay đổi chưa lưu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            e.Cancel = discard != MessageBoxResult.Yes;
        }
        else
        {
            var confirm = MessageBox.Show(
                this,
                "Bạn có chắc muốn thoát ứng dụng?",
                "Thoát ứng dụng",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            e.Cancel = confirm != MessageBoxResult.Yes;
        }

        _suppressClosePrompt = !e.Cancel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var uiScale = UiPreferenceService.LoadUiScale();
        UiScaleManager.SetScale(uiScale);
        UpdateUiScaleMenu(uiScale);
        await _viewModel.InitializeAsync();
    }

    private void UiScaleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }
            || !double.TryParse(tag, NumberStyles.Number, CultureInfo.InvariantCulture, out var scale))
        {
            return;
        }

        UiScaleManager.SetScale(scale);
        UiPreferenceService.SaveUiScale(scale);
        UpdateUiScaleMenu(scale);
    }

    private void UpdateUiScaleMenu(double scale)
    {
        foreach (var item in UiScaleMenu.Items.OfType<MenuItem>())
        {
            item.IsCheckable = true;
            item.IsChecked = item.Tag is string tag
                && double.TryParse(tag, NumberStyles.Number, CultureInfo.InvariantCulture, out var itemScale)
                && Math.Abs(itemScale - scale) < 0.001d;
        }
    }

    private void About_OnClick(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void DataGrid_OnLoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private void DashboardGrid_OnCopyingRowClipboardContent(object sender, DataGridRowClipboardEventArgs e)
    {
        for (var i = 0; i < e.ClipboardRowContent.Count; i++)
        {
            var item = e.ClipboardRowContent[i];

            // 1. Sửa tiêu đề cột nếu tiêu đề là StackPanel chứa nút phễu lọc
            object? newHeader = item.Column.Header;
            if (item.Column.Header is StackPanel headerPanel)
            {
                var titleBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault();
                if (titleBlock is not null)
                {
                    newHeader = titleBlock.Text;
                }
            }

            // 2. Sửa nội dung ô nếu nội dung bị biến thành tên Control (vd: StackPanel, TextBlock,...)
            var newContent = FormatClipboardCellContent(item.Item, item.Column, item.Content);

            e.ClipboardRowContent[i] = new DataGridClipboardCellContent(
                item.Item,
                item.Column,
                newContent);
        }
    }

    private static object? FormatClipboardCellContent(object rowItem, DataGridColumn column, object? rawContent)
    {
        if (rawContent is null)
        {
            return string.Empty;
        }

        var str = rawContent.ToString() ?? string.Empty;
        if (!str.StartsWith("System.Windows.", StringComparison.Ordinal))
        {
            return rawContent;
        }

        // Nếu WPF trả về tên Type của FrameworkElement, tự lấy dữ liệu thực từ Row ViewModel qua Binding
        if (column is DataGridTextColumn textCol && textCol.Binding is Binding b)
        {
            var propName = b.Path?.Path;
            if (!string.IsNullOrEmpty(propName) && rowItem is not null)
            {
                var prop = rowItem.GetType().GetProperty(propName);
                if (prop is not null)
                {
                    var val = prop.GetValue(rowItem);
                    if (val is not null)
                    {
                        if (!string.IsNullOrEmpty(b.StringFormat))
                        {
                            return string.Format(CultureInfo.CurrentCulture, b.StringFormat, val);
                        }

                        return val.ToString();
                    }
                }
            }
        }

        if (rowItem is DashboardRowViewModel dashRow)
        {
            var headerText = column.Header?.ToString();
            if (column.Header is StackPanel sp)
            {
                headerText = sp.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
            }

            return headerText switch
            {
                "Danh mục" => dashRow.Category,
                "Tên mặt hàng" => dashRow.ItemName,
                "DVT" => dashRow.Unit,
                "Đơn giá" => dashRow.UnitPrice.ToString("N0"),
                "Loại sử dụng" => dashRow.UsageTypeDisplay,
                "Tồn đầu năm" => dashRow.OpeningQuantity.ToString("N0"),
                "Tổng nhập" => dashRow.ReceivedQuantity.ToString("N0"),
                "Tổng xuất" => dashRow.IssuedQuantity.ToString("N0"),
                "Tồn cuối năm" => dashRow.ClosingQuantity.ToString("N0"),
                "Thành tiền" => dashRow.Amount.ToString("N0"),
                "Ghi chú" => dashRow.Note,
                _ => string.Empty
            };
        }

        return string.Empty;
    }

    private void SelectAllOpeningLines_OnChanged(object sender, RoutedEventArgs e)
    {
        var isSelected = sender is CheckBox { IsChecked: true };
        foreach (var line in _viewModel.OpeningBalanceLines)
        {
            line.IsSelected = isSelected;
        }
    }

    private void SelectAllReceipts_OnChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllReceipts(sender is CheckBox { IsChecked: true });
    }

    private void SelectAllIssues_OnChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllIssues(sender is CheckBox { IsChecked: true });
    }

    private void SelectAllManagedUnitIssues_OnChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectAllManagedUnitIssues(sender is CheckBox { IsChecked: true });
    }

    private void ReceiptsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is Receipt receipt)
        {
            _viewModel.PreviewReceiptCommand.Execute(receipt);
        }
    }

    private void IssuesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is Issue issue)
        {
            _viewModel.PreviewIssueCommand.Execute(issue);
        }
    }

    private void DashboardGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            return;
        }

        if (DataContext is not MainViewModel vm || !vm.DashboardEditUnlocked)
        {
            return;
        }

        if (e.Row.Item is not DashboardRowViewModel row)
        {
            return;
        }

        TryCommitBindingFromEditor(e.EditingElement as FrameworkElement);

        Dispatcher.BeginInvoke(
            () => _ = vm.CommitDashboardRowEditAsync(row),
            DispatcherPriority.ApplicationIdle);
    }

    private void MainDraftGrid_OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            return;
        }

        TryCommitBindingFromEditor(e.EditingElement as FrameworkElement);
        Dispatcher.BeginInvoke(_viewModel.NotifyMainDraftState, DispatcherPriority.ApplicationIdle);
    }

    private static void TryCommitBindingFromEditor(FrameworkElement? fe)
    {
        if (fe is null)
        {
            return;
        }

        if (fe is TextBox tb)
        {
            BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
            return;
        }

        if (fe is ComboBox cb)
        {
            var sv = BindingOperations.GetBindingExpression(cb, ComboBox.SelectedValueProperty);
            if (sv is not null)
            {
                sv.UpdateSource();
            }
            else
            {
                BindingOperations.GetBindingExpression(cb, ComboBox.SelectedItemProperty)?.UpdateSource();
            }

            return;
        }

        var innerCombo = FindVisualChild<ComboBox>(fe);
        if (innerCombo is not null)
        {
            TryCommitBindingFromEditor(innerCombo);
            return;
        }

        var innerTb = FindVisualChild<TextBox>(fe);
        TryCommitBindingFromEditor(innerTb);
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

    private void DashboardGrid_OnCanPaste(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _viewModel.DashboardEditUnlocked && Clipboard.ContainsText();
        e.Handled = true;
    }

    private async void DashboardGrid_OnPasteExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (!_viewModel.DashboardEditUnlocked || !Clipboard.ContainsText())
        {
            return;
        }

        if (sender is not DataGrid grid || grid.CurrentCell.Item is null || grid.CurrentColumn is null)
        {
            return;
        }

        var text = Clipboard.GetText();
        var rowsStr = text.Replace("\r\n", "\n").TrimEnd('\n', '\r').Split('\n');
        var matrix = rowsStr.Select(line => (IReadOnlyList<string>)line.Split('\t')).ToList();
        if (matrix.Count == 0)
        {
            return;
        }

        var ordered = new List<object?>();
        foreach (var x in grid.Items)
        {
            ordered.Add(x);
        }

        var startRow = grid.Items.IndexOf(grid.CurrentCell.Item);
        var startCol = grid.CurrentColumn.DisplayIndex;
        await _viewModel.ApplyDashboardPasteAsync(ordered, startRow, startCol, matrix);
    }

    private void SubordinateUnitsGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _subordinateDragSource = null;
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var handle = FindAncestorWithTag(source, "UnitDragHandle");
        if (handle is null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(handle);
        if (row?.Item is not SubordinateUnit unit)
        {
            return;
        }

        _subordinateDragSource = unit;
        _subordinateDragStart = e.GetPosition(null);
        _viewModel.SelectedSubordinateUnit = unit;
    }

    private void SubordinateUnitsGrid_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_subordinateDragSource is null
            || e.LeftButton != MouseButtonState.Pressed
            || sender is not DataGrid grid)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _subordinateDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _subordinateDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var payload = _subordinateDragSource;
        _subordinateDragSource = null;
        DragDrop.DoDragDrop(grid, payload, DragDropEffects.Move);
    }

    private void SubordinateUnitsGrid_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(SubordinateUnit))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void SubordinateUnitsGrid_OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (e.Effects.HasFlag(DragDropEffects.Move))
        {
            Mouse.SetCursor(Cursors.SizeAll);
            e.UseDefaultCursors = false;
            e.Handled = true;
        }
    }

    private void SubordinateUnitsGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SubordinateUnit))
            || e.Data.GetData(typeof(SubordinateUnit)) is not SubordinateUnit source
            || e.OriginalSource is not DependencyObject visual)
        {
            return;
        }

        var targetRow = FindVisualParent<DataGridRow>(visual);
        if (targetRow?.Item is not SubordinateUnit target || ReferenceEquals(source, target))
        {
            return;
        }

        var targetIndex = _viewModel.SubordinateUnits.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        _viewModel.ReorderSubordinateUnit(source, targetIndex);
        RefreshSubordinateUnitRowNumbers();
        e.Handled = true;
    }

    private void RefreshSubordinateUnitRowNumbers()
    {
        foreach (var item in SubordinateUnitsGrid.Items)
        {
            if (SubordinateUnitsGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
            {
                row.Header = (row.GetIndex() + 1).ToString();
            }
        }
    }

    private static DependencyObject? FindAncestorWithTag(DependencyObject? current, string tag)
    {
        while (current is not null)
        {
            if (current is FrameworkElement { Tag: string value } && value == tag)
            {
                return current;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}