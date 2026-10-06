using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Inventory.Domain.Models;

namespace Inventory.App.Views;

/// <summary>
/// Ô gõ mã/tên mặt hàng: TextBox thuần (IME tiếng Việt ổn định) + danh sách gợi ý lọc theo chuỗi gần đúng.
/// </summary>
public partial class ItemSuggestBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(ItemSuggestBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(ItemSuggestBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    private readonly ObservableCollection<Item> _suggestions = [];
    private bool _suppressTextSync;
    private bool _imeComposing;
    private bool _applyingSuggestion;
    private DispatcherTimer? _filterTimer;
    private int _highlightIndex;

    public ItemSuggestBox()
    {
        InitializeComponent();
        SuggestList.ItemsSource = _suggestions;
        QueryBox.AddHandler(
            TextCompositionManager.PreviewTextInputStartEvent,
            new TextCompositionEventHandler((_, _) => _imeComposing = true),
            handledEventsToo: true);
        QueryBox.AddHandler(
            TextCompositionManager.TextInputStartEvent,
            new TextCompositionEventHandler((_, _) => _imeComposing = true),
            handledEventsToo: true);
        QueryBox.AddHandler(
            TextCompositionManager.TextInputUpdateEvent,
            new TextCompositionEventHandler((_, _) => _imeComposing = true),
            handledEventsToo: true);
        QueryBox.AddHandler(
            TextCompositionManager.TextInputEvent,
            new TextCompositionEventHandler((_, _) =>
            {
                _imeComposing = false;
                ScheduleFilter();
            }),
            handledEventsToo: true);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Popup gợi ý đang mở — DataGrid không được cướp phím ↑↓/Enter.</summary>
    public bool IsSuggestionOpen => SuggestPopup.IsOpen && _suggestions.Count > 0;

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemSuggestBox box || box._suppressTextSync)
        {
            return;
        }

        var next = e.NewValue as string ?? string.Empty;
        if (!string.Equals(box.QueryBox.Text, next, StringComparison.Ordinal))
        {
            box.QueryBox.Text = next;
            box.QueryBox.CaretIndex = next.Length;
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ItemSuggestBox box)
        {
            box.ScheduleFilter();
        }
    }

    private void QueryBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextSync)
        {
            return;
        }

        _suppressTextSync = true;
        Text = QueryBox.Text ?? string.Empty;
        _suppressTextSync = false;

        if (_imeComposing)
        {
            return;
        }

        ScheduleFilter();
    }

    private void ScheduleFilter()
    {
        _filterTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _filterTimer.Tick -= FilterTimer_OnTick;
        _filterTimer.Tick += FilterTimer_OnTick;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void FilterTimer_OnTick(object? sender, EventArgs e)
    {
        _filterTimer?.Stop();
        RefreshSuggestions(resetHighlight: true);
    }

    private void RefreshSuggestions(bool resetHighlight)
    {
        if (_applyingSuggestion)
        {
            return;
        }

        var query = QueryBox.Text?.Trim() ?? string.Empty;
        _suggestions.Clear();

        if (ItemsSource is null || query.Length == 0)
        {
            SuggestPopup.IsOpen = false;
            _highlightIndex = 0;
            return;
        }

        var normalizedQuery = Normalize(query);
        const int maxResults = 40;
        foreach (var item in EnumerateItems())
        {
            if (!Matches(item, query, normalizedQuery))
            {
                continue;
            }

            _suggestions.Add(item);
            if (_suggestions.Count >= maxResults)
            {
                break;
            }
        }

        SuggestPopup.IsOpen = _suggestions.Count > 0 && (QueryBox.IsKeyboardFocusWithin || IsKeyboardFocusWithin);
        if (_suggestions.Count == 0)
        {
            _highlightIndex = 0;
            return;
        }

        if (resetHighlight)
        {
            _highlightIndex = 0;
        }
        else
        {
            _highlightIndex = Math.Clamp(_highlightIndex, 0, _suggestions.Count - 1);
        }

        ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        if (_suggestions.Count == 0)
        {
            return;
        }

        _highlightIndex = Math.Clamp(_highlightIndex, 0, _suggestions.Count - 1);
        SuggestList.SelectedIndex = _highlightIndex;
        SuggestList.ScrollIntoView(_suggestions[_highlightIndex]);
    }

    private IEnumerable<Item> EnumerateItems()
    {
        if (ItemsSource is null)
        {
            yield break;
        }

        foreach (var entry in ItemsSource)
        {
            if (entry is Item item)
            {
                yield return item;
            }
        }
    }

    private static bool Matches(Item item, string rawQuery, string normalizedQuery)
    {
        if (item.ItemCode.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)
            || item.ItemName.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)
            || item.UsageType.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(item.Note)
                && item.Note.Contains(rawQuery, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var code = Normalize(item.ItemCode);
        var name = Normalize(item.ItemName);
        var category = Normalize(item.Category);
        var usage = Normalize(item.UsageType);
        var note = Normalize(item.Note ?? string.Empty);
        return code.Contains(normalizedQuery, StringComparison.Ordinal)
               || name.Contains(normalizedQuery, StringComparison.Ordinal)
               || category.Contains(normalizedQuery, StringComparison.Ordinal)
               || usage.Contains(normalizedQuery, StringComparison.Ordinal)
               || note.Contains(normalizedQuery, StringComparison.Ordinal);
    }

    private static string Normalize(string value)
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

    private void QueryBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SuggestPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (!IsSuggestionOpen)
        {
            if (e.Key is Key.Down && _suggestions.Count > 0)
            {
                SuggestPopup.IsOpen = true;
                ApplyHighlight();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _highlightIndex = Math.Min(_highlightIndex + 1, _suggestions.Count - 1);
                ApplyHighlight();
                e.Handled = true;
                break;
            case Key.Up:
                _highlightIndex = Math.Max(_highlightIndex - 1, 0);
                ApplyHighlight();
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                var selected = GetHighlightedItem();
                if (selected is not null)
                {
                    ApplySuggestion(selected);
                    e.Handled = true;
                }
                break;
        }
    }

    private Item? GetHighlightedItem()
    {
        if (_suggestions.Count == 0)
        {
            return null;
        }

        if (SuggestList.SelectedItem is Item selected)
        {
            return selected;
        }

        return _suggestions[Math.Clamp(_highlightIndex, 0, _suggestions.Count - 1)];
    }

    private void SuggestPopup_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindItemFromSource(e.OriginalSource as DependencyObject);
        if (item is null)
        {
            return;
        }

        ApplySuggestion(item);
        e.Handled = true;
    }

    private static Item? FindItemFromSource(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ListBoxItem { DataContext: Item listItem })
            {
                return listItem;
            }

            if (current is FrameworkElement { DataContext: Item dataItem })
            {
                return dataItem;
            }

            current = GetVisualOrLogicalParent(current);
        }

        return null;
    }

    /// <summary>
    /// Click vào <see cref="System.Windows.Documents.Run"/> (nội dung TextBlock) không phải Visual —
    /// phải leo qua Parent logic thay vì VisualTreeHelper.
    /// </summary>
    private static DependencyObject? GetVisualOrLogicalParent(DependencyObject current) =>
        current switch
        {
            Visual visual => VisualTreeHelper.GetParent(visual),
            FrameworkContentElement content => content.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };

    /// <summary>
    /// Gõ thay nội dung từ DataGrid (Excel-like): ghi đè Text + caret cuối, không SelectAll.
    /// </summary>
    public void ReplaceTextFromTypeOver(string text)
    {
        text ??= string.Empty;
        _suppressTextSync = true;
        try
        {
            QueryBox.Text = text;
            Text = text;
        }
        finally
        {
            _suppressTextSync = false;
        }

        QueryBox.CaretIndex = text.Length;
        QueryBox.SelectionLength = 0;
        if (!QueryBox.IsKeyboardFocusWithin)
        {
            QueryBox.Focus();
        }

        if (!_imeComposing)
        {
            ScheduleFilter();
        }
    }

    private void ApplySuggestion(Item item)
    {
        _applyingSuggestion = true;
        try
        {
            _suppressTextSync = true;
            QueryBox.Text = item.ItemCode;
            Text = item.ItemCode;
            _suppressTextSync = false;
            SuggestPopup.IsOpen = false;

            GetBindingExpression(TextProperty)?.UpdateSource();
            EndDataGridEditKeepCellFocus();
        }
        finally
        {
            _applyingSuggestion = false;
        }
    }

    /// <summary>Commit giá trị rồi thoát chế độ sửa, chỉ giữ focus ô (không mở editor).</summary>
    private void EndDataGridEditKeepCellFocus()
    {
        var grid = FindAncestor<DataGrid>(this);
        if (grid is null)
        {
            return;
        }

        var cellInfo = grid.CurrentCell;
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);

        if (!cellInfo.IsValid)
        {
            return;
        }

        grid.CurrentCell = cellInfo;
        grid.SelectedCells.Clear();
        grid.SelectedCells.Add(cellInfo);

        grid.Dispatcher.BeginInvoke(
            () =>
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(cellInfo.Item) is not DataGridRow row
                    || cellInfo.Column is null)
                {
                    return;
                }

                row.ApplyTemplate();
                var presenter = FindVisualChild<DataGridCellsPresenter>(row);
                if (presenter?.ItemContainerGenerator.ContainerFromIndex(cellInfo.Column.DisplayIndex)
                    is DataGridCell cell)
                {
                    cell.Focus();
                }
                else
                {
                    grid.Focus();
                }
            },
            DispatcherPriority.Input);
    }

    private void QueryBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_applyingSuggestion)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (_applyingSuggestion || SuggestPopup.IsMouseOver || QueryBox.IsKeyboardFocusWithin)
                {
                    return;
                }

                SuggestPopup.IsOpen = false;
            },
            DispatcherPriority.Background);
    }

    private void UserControl_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(QueryBox.Text, Text ?? string.Empty, StringComparison.Ordinal))
        {
            // Chỉ sync khi QueryBox trống / khớp binding — tránh đè ký tự đang gõ type-over.
            if (string.IsNullOrEmpty(QueryBox.Text))
            {
                _suppressTextSync = true;
                QueryBox.Text = Text ?? string.Empty;
                _suppressTextSync = false;
            }
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (!IsKeyboardFocusWithin && !QueryBox.IsKeyboardFocusWithin)
                {
                    QueryBox.Focus();
                }

                if (!string.IsNullOrEmpty(QueryBox.Text) && QueryBox.SelectionLength == 0)
                {
                    QueryBox.CaretIndex = QueryBox.Text.Length;
                }
            },
            DispatcherPriority.Input);
    }

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
