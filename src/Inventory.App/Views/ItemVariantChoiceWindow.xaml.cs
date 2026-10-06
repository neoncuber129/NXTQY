using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Inventory.Domain.Models;
using Inventory.Domain.Services;

namespace Inventory.App.Views;

public enum ItemVariantChoiceAction
{
    Cancel,
    UseExisting,
    CreateNew
}

public partial class ItemVariantChoiceWindow : Window
{
    private static readonly Brush SelectedBg = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8));
    private static readonly Brush SelectedFg = Brushes.White;
    private static readonly Brush IdleBg = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly Brush IdleFg = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly Brush IdleBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
    private static readonly Brush SelectedBorder = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0xAF));
    private static readonly Brush DraftIdleBg = new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xED));
    private static readonly Brush DraftIdleBorder = new SolidColorBrush(Color.FromRgb(0xFD, 0xBA, 0x74));
    private static readonly Brush DraftSelectedBg = new SolidColorBrush(Color.FromRgb(0xC2, 0x41, 0x0C));
    private static readonly Brush DraftSelectedBorder = new SolidColorBrush(Color.FromRgb(0x9A, 0x34, 0x12));

    private readonly string _itemName;
    private readonly decimal _unitPrice;
    private readonly IReadOnlyList<Item> _candidates;
    private readonly List<ChoiceChip> _categoryChips = [];
    private readonly List<ChoiceChip> _usageChips = [];
    private readonly HashSet<string> _existingCategories =
        new(StringComparer.OrdinalIgnoreCase);

    private string _selectedCategory = string.Empty;
    private string _selectedUsage = string.Empty;

    public ItemVariantChoiceAction Choice { get; private set; } = ItemVariantChoiceAction.Cancel;
    public Item? SelectedItem { get; private set; }
    public string SelectedCategory => _selectedCategory;
    public string SelectedUsageType => _selectedUsage;

    public ItemVariantChoiceWindow(
        string lineLabel,
        string itemName,
        decimal unitPrice,
        string? draftCategory,
        string? draftUsageType,
        string? draftUnit,
        IReadOnlyList<Item> candidates)
    {
        InitializeComponent();
        _ = draftUnit;
        _ = lineLabel;
        _itemName = itemName?.Trim() ?? string.Empty;
        _unitPrice = unitPrice;
        _candidates = candidates;

        ItemNameText.Text = string.IsNullOrWhiteSpace(_itemName) ? "(trống)" : _itemName;
        UnitPriceText.Text = $"{_unitPrice:N0} đ";

        var draftCat = ItemVariantKey.NormalizeCategory(draftCategory);
        var draftUsage = ItemVariantKey.NormalizeUsageType(draftUsageType);

        var categories = new List<(string Value, string Label, string Source, bool IsDraftOnly)>();
        foreach (var item in candidates)
        {
            var value = ItemVariantKey.NormalizeCategory(item.Category);
            _existingCategories.Add(value);
            if (categories.Any(c => string.Equals(c.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            categories.Add((value, DisplayCategory(value), $"đã có — mã {item.ItemCode}", false));
        }

        var draftIsExisting = categories.Any(c =>
            string.Equals(c.Value, draftCat, StringComparison.OrdinalIgnoreCase));
        if (draftIsExisting)
        {
            EnsureDraftLabeled(categories, draftCat, DisplayCategory(draftCat), "đã có / vừa nhập trên phiếu");
        }
        else
        {
            // Đưa mục mới xuống cuối, cảnh báo không nên chọn trừ khi cần biến thể.
            categories.Add((
                draftCat,
                DisplayCategory(draftCat),
                "mới trên phiếu — chỉ chọn nếu thật sự khác danh mục",
                true));
        }

        var usages = new List<(string Value, string Label, string Source, bool IsDraftOnly)>();
        foreach (var item in candidates)
        {
            var value = ItemVariantKey.NormalizeUsageType(item.UsageType);
            if (usages.Any(u => u.Value == value))
            {
                continue;
            }

            usages.Add((value, DisplayUsage(value), $"đã có — mã {item.ItemCode}", false));
        }

        EnsureDraftUsage(usages, draftUsage);

        foreach (var (value, label, source, isDraftOnly) in categories)
        {
            var chip = CreateChip(label, source, value, isCategory: true, isDraftOnly);
            _categoryChips.Add(chip);
            CategoryChoicesPanel.Children.Add(chip.Button);
        }

        foreach (var (value, label, source, isDraftOnly) in usages)
        {
            var chip = CreateChip(label, source, value, isCategory: false, isDraftOnly);
            _usageChips.Add(chip);
            UsageChoicesPanel.Children.Add(chip.Button);
        }

        // Mặc định ưu tiên danh mục đã có (tránh tạo biến thể chỉ vì gõ danh mục lệch).
        (_selectedCategory, _selectedUsage) = PickDefaultSelection(draftCat, draftUsage);
        RefreshChipStyles();
        RefreshResultPreview();
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sau khi UI scale áp LayoutTransform, đảm bảo cửa sổ không vượt màn và nút còn trong vùng nhìn.
        var work = SystemParameters.WorkArea;
        MaxHeight = Math.Max(420, work.Height - 40);
        if (ActualHeight > MaxHeight)
        {
            SizeToContent = SizeToContent.Manual;
            Height = MaxHeight;
        }

        ConfirmButton.BringIntoView();
    }

    private (string Category, string Usage) PickDefaultSelection(string draftCat, string draftUsage)
    {
        // 1) Danh mục đã có + loại đang nhập → nếu khớp mã sẵn có thì chọn luôn.
        foreach (var existingCat in _existingCategories)
        {
            if (_candidates.Any(x =>
                    ItemVariantKey.Matches(x, _itemName, _unitPrice, existingCat, draftUsage)))
            {
                return (existingCat, draftUsage);
            }
        }

        // 2) Giữ loại đang nhập, lấy danh mục đầu tiên đã có (hướng gắn mã / ít phát sinh).
        if (_existingCategories.Count > 0)
        {
            var preferredCat = _existingCategories.Contains(draftCat)
                ? draftCat
                : _existingCategories.First();
            return (preferredCat, draftUsage);
        }

        // 3) Không có danh mục sẵn → dùng draft.
        return (draftCat, draftUsage);
    }

    private static void EnsureDraftLabeled(
        List<(string Value, string Label, string Source, bool IsDraftOnly)> options,
        string draftValue,
        string draftLabel,
        string draftSource)
    {
        var idx = options.FindIndex(o =>
            string.Equals(o.Value, draftValue, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
        {
            options.Add((draftValue, draftLabel, draftSource, false));
            return;
        }

        var existing = options[idx];
        if (!existing.Source.Contains("vừa nhập", StringComparison.Ordinal))
        {
            options[idx] = (existing.Value, existing.Label, draftSource, false);
        }
    }

    private static void EnsureDraftUsage(
        List<(string Value, string Label, string Source, bool IsDraftOnly)> usages,
        string draftUsage)
    {
        if (usages.Any(u => u.Value == draftUsage))
        {
            return;
        }

        usages.Add((draftUsage, DisplayUsage(draftUsage), "theo dòng phiếu", false));
    }

    private ChoiceChip CreateChip(string label, string source, string value, bool isCategory, bool isDraftOnly)
    {
        var button = new Button
        {
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(14, 10, 14, 10),
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = isDraftOnly ? DraftIdleBg : IdleBg,
            BorderBrush = isDraftOnly ? DraftIdleBorder : IdleBorder,
            BorderThickness = new Thickness(1.5),
            Focusable = true,
            MaxWidth = 280
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = IdleFg,
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = source,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            TextWrapping = TextWrapping.Wrap
        });
        button.Content = content;

        var chip = new ChoiceChip(button, value, label, source, isCategory, isDraftOnly);
        button.Click += (_, _) =>
        {
            if (isCategory)
            {
                _selectedCategory = value;
            }
            else
            {
                _selectedUsage = value;
            }

            RefreshChipStyles();
            RefreshResultPreview();
        };

        return chip;
    }

    private void RefreshChipStyles()
    {
        foreach (var chip in _categoryChips)
        {
            ApplyChipStyle(chip, string.Equals(chip.Value, _selectedCategory, StringComparison.OrdinalIgnoreCase)
                                 || chip.Value == _selectedCategory);
        }

        foreach (var chip in _usageChips)
        {
            ApplyChipStyle(chip, chip.Value == _selectedUsage);
        }
    }

    private static void ApplyChipStyle(ChoiceChip chip, bool selected)
    {
        if (chip.IsDraftOnly)
        {
            chip.Button.Background = selected ? DraftSelectedBg : DraftIdleBg;
            chip.Button.BorderBrush = selected ? DraftSelectedBorder : DraftIdleBorder;
        }
        else
        {
            chip.Button.Background = selected ? SelectedBg : IdleBg;
            chip.Button.BorderBrush = selected ? SelectedBorder : IdleBorder;
        }

        if (chip.Button.Content is StackPanel panel)
        {
            if (panel.Children[0] is TextBlock title)
            {
                title.Foreground = selected ? SelectedFg : IdleFg;
            }

            if (panel.Children[1] is TextBlock subtitle)
            {
                subtitle.Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE))
                    : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
            }
        }
    }

    private void RefreshResultPreview()
    {
        var match = _candidates.FirstOrDefault(x =>
            ItemVariantKey.Matches(x, _itemName, _unitPrice, _selectedCategory, _selectedUsage));
        var categoryIsExisting = _existingCategories.Contains(_selectedCategory);

        if (match is not null)
        {
            ResultPreviewText.Text =
                $"Dùng mặt hàng đã có — mã {match.ItemCode}\n"
                + $"Danh mục: {DisplayCategory(_selectedCategory)} · Loại: {DisplayUsage(_selectedUsage)}";
            HintText.Text = "Tổ hợp này trùng biến thể sẵn có → phiếu sẽ gắn mã đó (không tạo mã mới).";
            return;
        }

        ResultPreviewText.Text =
            "Tạo mã hàng mới (biến thể riêng, tồn tách biệt)\n"
            + $"Danh mục: {DisplayCategory(_selectedCategory)} · Loại: {DisplayUsage(_selectedUsage)}";

        HintText.Text = !categoryIsExisting && _existingCategories.Count > 0
            ? "Bạn đang chọn danh mục mới trên phiếu. Nếu chỉ khác cách gõ, hãy chọn lại danh mục «đã có» để tránh tạo mã thừa."
            : "Tổ hợp này chưa có → app sẽ tạo mã mới khi lưu. Nên chỉ tạo khi danh mục/loại thật sự khác.";
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        var match = _candidates.FirstOrDefault(x =>
            ItemVariantKey.Matches(x, _itemName, _unitPrice, _selectedCategory, _selectedUsage));
        if (match is not null)
        {
            SelectedItem = match;
            Choice = ItemVariantChoiceAction.UseExisting;
        }
        else
        {
            SelectedItem = null;
            Choice = ItemVariantChoiceAction.CreateNew;
        }

        DialogResult = true;
    }

    private static string DisplayCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? "(trống)" : category.Trim();

    private static string DisplayUsage(string? usageType)
    {
        var normalized = ItemVariantKey.NormalizeUsageType(usageType);
        return string.IsNullOrWhiteSpace(normalized) ? "tự mua sắm" : "trên cấp";
    }

    private sealed class ChoiceChip(
        Button button,
        string value,
        string label,
        string source,
        bool isCategory,
        bool isDraftOnly)
    {
        public Button Button { get; } = button;
        public string Value { get; } = value;
        public string Label { get; } = label;
        public string Source { get; } = source;
        public bool IsCategory { get; } = isCategory;
        public bool IsDraftOnly { get; } = isDraftOnly;
    }
}
