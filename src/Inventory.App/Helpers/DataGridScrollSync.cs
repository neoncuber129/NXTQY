using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Inventory.App.Helpers;

/// <summary>
/// Đồng bộ cuộn dọc / chọn dòng giữa hai DataGrid (mỗi bên có thanh cuộn ngang riêng).
/// </summary>
public static class DataGridScrollSync
{
    private static readonly DependencyProperty SyncHookedProperty =
        DependencyProperty.RegisterAttached(
            "SyncHooked",
            typeof(bool),
            typeof(DataGridScrollSync),
            new PropertyMetadata(false));

    public static void Attach(DataGrid left, DataGrid right)
    {
        if (left is null || right is null)
        {
            return;
        }

        void TryWire()
        {
            var leftViewer = FindScrollViewer(left);
            var rightViewer = FindScrollViewer(right);
            if (leftViewer is null || rightViewer is null)
            {
                return;
            }

            if ((bool)leftViewer.GetValue(SyncHookedProperty))
            {
                return;
            }

            leftViewer.SetValue(SyncHookedProperty, true);
            rightViewer.SetValue(SyncHookedProperty, true);

            leftViewer.ScrollChanged += (_, e) =>
            {
                if (e.VerticalChange == 0)
                {
                    return;
                }

                if (Math.Abs(rightViewer.VerticalOffset - leftViewer.VerticalOffset) > 0.5)
                {
                    rightViewer.ScrollToVerticalOffset(leftViewer.VerticalOffset);
                }
            };

            rightViewer.ScrollChanged += (_, e) =>
            {
                if (e.VerticalChange == 0)
                {
                    return;
                }

                if (Math.Abs(leftViewer.VerticalOffset - rightViewer.VerticalOffset) > 0.5)
                {
                    leftViewer.ScrollToVerticalOffset(rightViewer.VerticalOffset);
                }
            };
        }

        left.Loaded += (_, _) => left.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, TryWire);
        right.Loaded += (_, _) => right.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, TryWire);
        left.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, TryWire);
    }

    /// <summary>Đồng bộ chọn dòng (và ô hiện tại) sang DataGrid còn lại và đưa dòng vào khung nhìn.</summary>
    public static void SyncFullRowSelection(DataGrid source, DataGrid target)
    {
        if (source is null || target is null)
        {
            return;
        }

        var item = source.CurrentCell.IsValid
            ? source.CurrentCell.Item
            : source.SelectedItem;
        if (item is null)
        {
            return;
        }

        if (!ReferenceEquals(target.SelectedItem, item))
        {
            target.SelectedItem = item;
        }

        if (target.Columns.Count > 0
            && (!target.CurrentCell.IsValid || !ReferenceEquals(target.CurrentCell.Item, item)))
        {
            var column = target.CurrentCell.Column
                ?? target.Columns.OrderBy(c => c.DisplayIndex).FirstOrDefault(c => !c.IsReadOnly)
                ?? target.Columns.OrderBy(c => c.DisplayIndex).First();
            var cell = new DataGridCellInfo(item, column);
            target.CurrentCell = cell;
            if (target.SelectionUnit != DataGridSelectionUnit.FullRow)
            {
                target.SelectedCells.Clear();
                target.SelectedCells.Add(cell);
            }
        }

        target.ScrollIntoView(item);
        source.ScrollIntoView(item);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv)
        {
            return sv;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindScrollViewer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
