using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Inventory.App.Views;

namespace Inventory.App.Helpers;

/// <summary>
/// Điều hướng ô kiểu Excel: mũi tên chuyển ô (không mở sửa ngay);
/// gõ phím mới vào chế độ sửa và thay nội dung.
/// </summary>
public static class ExcelLikeDataGridNavigation
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ExcelLikeDataGridNavigation),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty PendingEditTextProperty =
        DependencyProperty.RegisterAttached(
            "PendingEditText",
            typeof(string),
            typeof(ExcelLikeDataGridNavigation));

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
            grid.PreviewKeyDown += OnPreviewKeyDown;
            grid.PreviewTextInput += OnPreviewTextInput;
            grid.PreparingCellForEdit += OnPreparingCellForEdit;
            grid.GotKeyboardFocus += OnGotKeyboardFocus;
        }
        else
        {
            grid.PreviewKeyDown -= OnPreviewKeyDown;
            grid.PreviewTextInput -= OnPreviewTextInput;
            grid.PreparingCellForEdit -= OnPreparingCellForEdit;
            grid.GotKeyboardFocus -= OnGotKeyboardFocus;
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Giữ focus ở mức ô, tránh tự nhảy vào editor khi click chọn.
        if (sender is not DataGrid grid || grid.IsReadOnly)
        {
            return;
        }

        if (e.NewFocus is TextBox or ComboBox or DatePicker or CheckBox)
        {
            return;
        }

        if (e.NewFocus is DataGridCell cell
            && !cell.IsEditing
            && grid.CurrentCell.IsValid
            && ReferenceEquals(grid.CurrentCell.Item, cell.DataContext)
            && ReferenceEquals(grid.CurrentCell.Column, cell.Column))
        {
            cell.Focus();
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || e.Handled)
        {
            return;
        }

        if (IsInteractiveEditorFocused(e.OriginalSource))
        {
            // Để ItemSuggestBox tự xử lý ↑↓/Enter khi đang mở danh sách gợi ý.
            if (FindAncestor<ItemSuggestBox>(e.OriginalSource as DependencyObject) is { IsSuggestionOpen: true }
                && e.Key is Key.Up or Key.Down or Key.Enter or Key.Tab or Key.Escape)
            {
                return;
            }

            // Đang sửa TextBox: mũi tên thoát sửa + chuyển ô khi nội dung đang bôi đen (vừa gõ để thay).
            // Enter/Tab luôn commit + chuyển ô. Còn lại để editor tự xử lý.
            var isArrow = e.Key is Key.Up or Key.Down or Key.Left or Key.Right;
            if (e.Key is not (Key.Enter or Key.Tab) && !(isArrow && ShouldExitEditOnArrow(e)))
            {
                return;
            }
        }

        if (IsCheckBoxCell(e.OriginalSource) && e.Key is Key.Space)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(grid, 0, -1);
                break;
            case Key.Down:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(grid, 0, 1);
                break;
            case Key.Left:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(grid, -1, 0);
                break;
            case Key.Right:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(grid, 1, 0);
                break;
            case Key.Enter:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(grid, 0, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            case Key.Tab:
                CommitIfEditing(grid);
                e.Handled = MoveCurrentCell(
                    grid,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1,
                    0,
                    wrapToNextRow: true);
                break;
            case Key.F2:
                if (!grid.CurrentCell.IsValid || grid.CurrentCell.Column?.IsReadOnly == true)
                {
                    return;
                }

                grid.BeginEdit();
                e.Handled = true;
                break;
            case Key.Escape:
                if (grid.CurrentColumn is not null)
                {
                    grid.CancelEdit(DataGridEditingUnit.Cell);
                    grid.CancelEdit(DataGridEditingUnit.Row);
                    e.Handled = true;
                }
                break;
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not DataGrid grid || grid.IsReadOnly || e.Handled)
        {
            return;
        }

        if (IsInteractiveEditorFocused(e.OriginalSource) || IsCheckBoxCell(e.OriginalSource))
        {
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n" || e.Text == "\t")
        {
            return;
        }

        if (!grid.CurrentCell.IsValid
            || grid.CurrentCell.Column is null
            || grid.CurrentCell.Column.IsReadOnly)
        {
            return;
        }

        // Editor đã mở nhưng focus chưa vào TextBox (gõ nhanh): cộng dồn / ghi tiếp, không nuốt ký tự.
        if (IsCellAlreadyEditing(grid))
        {
            if (TryAppendToOpenEditor(grid, e.Text))
            {
                e.Handled = true;
            }

            return;
        }

        var existingPending = grid.GetValue(PendingEditTextProperty) as string ?? string.Empty;
        grid.SetValue(PendingEditTextProperty, existingPending + e.Text);
        if (!grid.BeginEdit())
        {
            grid.ClearValue(PendingEditTextProperty);
            return;
        }

        e.Handled = true;
    }

    private static bool TryAppendToOpenEditor(DataGrid grid, string text)
    {
        var cell = GetCell(grid, grid.CurrentCell);
        if (cell is null)
        {
            var existingPending = grid.GetValue(PendingEditTextProperty) as string ?? string.Empty;
            grid.SetValue(PendingEditTextProperty, existingPending + text);
            return true;
        }

        var suggest = FindVisualChild<ItemSuggestBox>(cell);
        if (suggest is not null)
        {
            var existingPending = grid.GetValue(PendingEditTextProperty) as string;
            if (!string.IsNullOrEmpty(existingPending))
            {
                grid.ClearValue(PendingEditTextProperty);
                suggest.ReplaceTextFromTypeOver(existingPending + text);
            }
            else
            {
                suggest.ReplaceTextFromTypeOver((suggest.Text ?? string.Empty) + text);
            }

            return true;
        }

        var textBox = FindVisualChild<TextBox>(cell) ?? cell.Content as TextBox;
        if (textBox is null || textBox.IsReadOnly)
        {
            var existingPending = grid.GetValue(PendingEditTextProperty) as string ?? string.Empty;
            grid.SetValue(PendingEditTextProperty, existingPending + text);
            return true;
        }

        var pending = grid.GetValue(PendingEditTextProperty) as string;
        if (!string.IsNullOrEmpty(pending))
        {
            grid.ClearValue(PendingEditTextProperty);
            textBox.Text = pending + text;
        }
        else
        {
            var start = textBox.SelectionLength > 0
                ? textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                : textBox.Text ?? string.Empty;
            var caret = textBox.SelectionLength > 0 ? textBox.SelectionStart : textBox.CaretIndex;
            if (caret < 0 || caret > start.Length)
            {
                caret = start.Length;
            }

            textBox.Text = start.Insert(caret, text);
        }

        textBox.CaretIndex = textBox.Text.Length;
        textBox.SelectionLength = 0;
        textBox.Focus();
        return true;
    }

    private static bool ShouldExitEditOnArrow(KeyEventArgs e)
    {
        // Chỉ chiếm mũi tên khi đang sửa TextBox và không mở dropdown.
        if (e.OriginalSource is not TextBox tb || tb.IsReadOnly)
        {
            return false;
        }

        if (FindAncestor<ComboBox>(tb) is not null || FindAncestor<DatePicker>(tb) is not null)
        {
            return false;
        }

        // Nếu bôi đen cả ô (vừa vào sửa bằng gõ phím) → mũi tên thoát như Excel.
        return tb.SelectionLength == tb.Text.Length || string.IsNullOrEmpty(tb.Text);
    }

    private static void OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var pendingAtPrepare = grid.GetValue(PendingEditTextProperty) as string;
        if (string.IsNullOrEmpty(pendingAtPrepare))
        {
            return;
        }

        // Đợi editor gắn visual tree; đọc lại pending lúc apply (có thể đã cộng thêm ký tự gõ nhanh).
        grid.Dispatcher.BeginInvoke(
            () =>
            {
                var pending = grid.GetValue(PendingEditTextProperty) as string ?? pendingAtPrepare;
                grid.ClearValue(PendingEditTextProperty);
                if (string.IsNullOrEmpty(pending))
                {
                    return;
                }

                var suggest = FindVisualChild<ItemSuggestBox>(e.EditingElement)
                    ?? e.EditingElement as ItemSuggestBox;
                if (suggest is not null)
                {
                    suggest.ReplaceTextFromTypeOver(pending);
                    return;
                }

                var textBox = FindVisualChild<TextBox>(e.EditingElement)
                    ?? e.EditingElement as TextBox;
                if (textBox is null)
                {
                    return;
                }

                textBox.Text = pending;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.SelectionLength = 0;
                textBox.Focus();
            },
            DispatcherPriority.Input);
    }

    private static bool MoveCurrentCell(DataGrid grid, int columnDelta, int rowDelta, bool wrapToNextRow = false)
    {
        if (!grid.CurrentCell.IsValid || grid.Items.Count == 0)
        {
            return false;
        }

        var columns = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToList();
        if (columns.Count == 0)
        {
            return false;
        }

        var currentColumn = grid.CurrentCell.Column;
        var currentDisplayIndex = currentColumn?.DisplayIndex ?? 0;
        var colPos = columns.FindIndex(c => c.DisplayIndex == currentDisplayIndex);
        if (colPos < 0)
        {
            colPos = 0;
        }

        var rowIndex = grid.Items.IndexOf(grid.CurrentCell.Item);
        if (rowIndex < 0)
        {
            return false;
        }

        var nextColPos = colPos + columnDelta;
        var nextRow = rowIndex + rowDelta;

        if (wrapToNextRow)
        {
            while (nextColPos < 0)
            {
                nextColPos += columns.Count;
                nextRow--;
            }

            while (nextColPos >= columns.Count)
            {
                nextColPos -= columns.Count;
                nextRow++;
            }
        }
        else
        {
            nextColPos = Math.Clamp(nextColPos, 0, columns.Count - 1);
        }

        nextRow = Math.Clamp(nextRow, 0, grid.Items.Count - 1);

        // Bỏ qua cột read-only khi di chuyển ngang; dọc thì giữ nguyên cột hiện tại
        // kể cả read-only (để mũi tên lên/xuống vẫn đi được).
        if (columnDelta != 0)
        {
            var direction = Math.Sign(columnDelta);
            var attempts = 0;
            while (attempts < columns.Count && columns[nextColPos].IsReadOnly)
            {
                nextColPos += direction;
                attempts++;
                if (wrapToNextRow)
                {
                    if (nextColPos < 0)
                    {
                        nextColPos = columns.Count - 1;
                        nextRow = Math.Clamp(nextRow - 1, 0, grid.Items.Count - 1);
                    }
                    else if (nextColPos >= columns.Count)
                    {
                        nextColPos = 0;
                        nextRow = Math.Clamp(nextRow + 1, 0, grid.Items.Count - 1);
                    }
                }
                else if (nextColPos < 0 || nextColPos >= columns.Count)
                {
                    return true; // chạm biên
                }
            }

            if (columns[nextColPos].IsReadOnly)
            {
                return true;
            }
        }

        var item = grid.Items[nextRow];
        var column = columns[nextColPos];
        grid.SelectedCells.Clear();
        var cellInfo = new DataGridCellInfo(item, column);
        grid.CurrentCell = cellInfo;
        grid.SelectedCells.Add(cellInfo);
        grid.ScrollIntoView(item, column);
        FocusCurrentCell(grid);
        return true;
    }

    private static void FocusCurrentCell(DataGrid grid)
    {
        grid.Dispatcher.BeginInvoke(
            () =>
            {
                if (!grid.CurrentCell.IsValid)
                {
                    return;
                }

                var cell = GetCell(grid, grid.CurrentCell);
                cell?.Focus();
            },
            DispatcherPriority.Input);
    }

    private static void CommitIfEditing(DataGrid grid)
    {
        if (!IsCellAlreadyEditing(grid))
        {
            return;
        }

        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private static bool IsCellAlreadyEditing(DataGrid grid)
    {
        if (!grid.CurrentCell.IsValid)
        {
            return false;
        }

        var cell = GetCell(grid, grid.CurrentCell);
        return cell?.IsEditing == true;
    }

    private static DataGridCell? GetCell(DataGrid grid, DataGridCellInfo info)
    {
        if (grid.ItemContainerGenerator.ContainerFromItem(info.Item) is not DataGridRow row)
        {
            return null;
        }

        var presenter = FindVisualChild<DataGridCellsPresenter>(row);
        if (presenter is null)
        {
            row.ApplyTemplate();
            presenter = FindVisualChild<DataGridCellsPresenter>(row);
        }

        if (presenter is null || info.Column is null)
        {
            return null;
        }

        return presenter.ItemContainerGenerator.ContainerFromIndex(info.Column.DisplayIndex) as DataGridCell;
    }

    private static bool IsInteractiveEditorFocused(object? source)
    {
        if (source is not DependencyObject d)
        {
            return false;
        }

        return FindAncestor<TextBox>(d) is not null
            || FindAncestor<ComboBox>(d) is not null
            || FindAncestor<DatePicker>(d) is not null
            || FindAncestor<PasswordBox>(d) is not null;
    }

    private static bool IsCheckBoxCell(object? source) =>
        source is DependencyObject d && FindAncestor<CheckBox>(d) is not null;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
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
}
