using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Inventory.App.Views;

public partial class CategoryOrderWindow : Window
{
    private static readonly string[] RomanNumerals =
    [
        "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X",
        "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX",
        "XXI", "XXII", "XXIII", "XXIV", "XXV", "XXVI", "XXVII", "XXVIII", "XXIX", "XXX"
    ];

    private readonly List<string> _initialCategories;
    public ObservableCollection<CategoryOrderItemViewModel> Items { get; } = [];

    public IReadOnlyList<string> OrderedCategories { get; private set; } = [];

    public CategoryOrderWindow(IEnumerable<string>? currentOrder, IEnumerable<string> allCategories)
    {
        InitializeComponent();

        var distinctAll = allCategories
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x.Trim(), "Tất cả", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _initialCategories = distinctAll.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        var orderList = currentOrder?
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x.Trim(), "Tất cả", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        // Thêm các category theo order đã lưu nếu chúng còn tồn tại
        var resolvedList = new List<string>();
        foreach (var cat in orderList)
        {
            var match = distinctAll.FirstOrDefault(x => string.Equals(x, cat, StringComparison.OrdinalIgnoreCase));
            if (match != null && !resolvedList.Contains(match, StringComparer.OrdinalIgnoreCase))
            {
                resolvedList.Add(match);
            }
        }

        // Bổ sung các category còn lại chưa có trong custom order
        foreach (var cat in _initialCategories)
        {
            if (!resolvedList.Contains(cat, StringComparer.OrdinalIgnoreCase))
            {
                resolvedList.Add(cat);
            }
        }

        foreach (var cat in resolvedList)
        {
            Items.Add(new CategoryOrderItemViewModel { Name = cat });
        }

        UpdateBadges();
        CategoryListBox.ItemsSource = Items;

        if (Items.Count > 0)
        {
            CategoryListBox.SelectedIndex = 0;
        }

        UpdateActionButtonsState();
        KeyDown += CategoryOrderWindow_KeyDown;
    }

    private void CategoryOrderWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            if (e.Key == Key.Up)
            {
                MoveUp();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveDown();
                e.Handled = true;
            }
        }
    }

    private void UpdateBadges()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].OrderBadge = i < RomanNumerals.Length ? RomanNumerals[i] : (i + 1).ToString();
        }
    }

    private void UpdateActionButtonsState()
    {
        var idx = CategoryListBox.SelectedIndex;
        var hasSelection = idx >= 0;
        MoveUpButton.IsEnabled = hasSelection && idx > 0;
        MoveTopButton.IsEnabled = hasSelection && idx > 0;
        MoveDownButton.IsEnabled = hasSelection && idx < Items.Count - 1;
        MoveBottomButton.IsEnabled = hasSelection && idx < Items.Count - 1;
    }

    private void CategoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateActionButtonsState();
    }

    private void MoveUp()
    {
        var idx = CategoryListBox.SelectedIndex;
        if (idx <= 0) return;

        Items.Move(idx, idx - 1);
        UpdateBadges();
        CategoryListBox.SelectedIndex = idx - 1;
        CategoryListBox.ScrollIntoView(CategoryListBox.SelectedItem);
    }

    private void MoveDown()
    {
        var idx = CategoryListBox.SelectedIndex;
        if (idx < 0 || idx >= Items.Count - 1) return;

        Items.Move(idx, idx + 1);
        UpdateBadges();
        CategoryListBox.SelectedIndex = idx + 1;
        CategoryListBox.ScrollIntoView(CategoryListBox.SelectedItem);
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e)
    {
        MoveUp();
    }

    private void MoveDownButton_Click(object sender, RoutedEventArgs e)
    {
        MoveDown();
    }

    private void MoveTopButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = CategoryListBox.SelectedIndex;
        if (idx <= 0) return;

        Items.Move(idx, 0);
        UpdateBadges();
        CategoryListBox.SelectedIndex = 0;
        CategoryListBox.ScrollIntoView(CategoryListBox.SelectedItem);
    }

    private void MoveBottomButton_Click(object sender, RoutedEventArgs e)
    {
        var idx = CategoryListBox.SelectedIndex;
        if (idx < 0 || idx >= Items.Count - 1) return;

        var target = Items.Count - 1;
        Items.Move(idx, target);
        UpdateBadges();
        CategoryListBox.SelectedIndex = target;
        CategoryListBox.ScrollIntoView(CategoryListBox.SelectedItem);
    }

    private void SortAlphabeticalButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedItemName = (CategoryListBox.SelectedItem as CategoryOrderItemViewModel)?.Name;
        var sorted = Items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }
        UpdateBadges();

        if (selectedItemName != null)
        {
            var match = Items.FirstOrDefault(x => string.Equals(x.Name, selectedItemName, StringComparison.OrdinalIgnoreCase));
            if (match != null) CategoryListBox.SelectedItem = match;
        }
    }

    private void ResetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        Items.Clear();
        foreach (var cat in _initialCategories)
        {
            Items.Add(new CategoryOrderItemViewModel { Name = cat });
        }
        UpdateBadges();
        if (Items.Count > 0) CategoryListBox.SelectedIndex = 0;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        OrderedCategories = Items.Select(x => x.Name).ToList();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

public sealed class CategoryOrderItemViewModel : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _orderBadge = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string OrderBadge
    {
        get => _orderBadge;
        set
        {
            if (_orderBadge != value)
            {
                _orderBadge = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
