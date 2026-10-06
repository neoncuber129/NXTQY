using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Inventory.App.Views;

public sealed class DashboardFilterValueItem(string canonicalValue, string display) : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string CanonicalValue { get; } = canonicalValue;
    public string Display { get; } = display;
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class DashboardColumnFilterWindow : Window
{
    private readonly ObservableCollection<DashboardFilterValueItem> _all = [];
    private readonly ObservableCollection<DashboardFilterValueItem> _filtered = [];

    public DashboardColumnFilterWindow(string columnTitle, IEnumerable<DashboardFilterValueItem> items)
    {
        InitializeComponent();
        Title = $"Lọc: {columnTitle}";
        foreach (var it in items)
        {
            _all.Add(it);
        }

        RebuildFiltered(string.Empty);
        ValuesList.ItemsSource = _filtered;
    }

    private void RebuildFiltered(string search)
    {
        _filtered.Clear();
        foreach (var it in _all)
        {
            if (string.IsNullOrWhiteSpace(search)
                || it.Display.Contains(search, StringComparison.OrdinalIgnoreCase)
                || it.CanonicalValue.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                _filtered.Add(it);
            }
        }
    }

    private void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        RebuildFiltered(SearchBox.Text ?? string.Empty);
    }

    private void SelectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var it in _all)
        {
            it.IsSelected = true;
        }
    }

    private void UnselectAll_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var it in _all)
        {
            it.IsSelected = false;
        }
    }

    private void ClearFilter_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var it in _all)
        {
            it.IsSelected = true;
        }

        DialogResult = true;
        Tag = null;
        Close();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var selected = _all.Where(x => x.IsSelected).Select(x => x.CanonicalValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allCanonical = _all.Select(x => x.CanonicalValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Chọn ít nhất một giá trị (giống Excel).", "Lọc cột", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.Count == allCanonical.Count)
        {
            Tag = null;
        }
        else
        {
            Tag = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        }

        DialogResult = true;
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
