using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Inventory.App.Views;

public sealed record DuplicateUnitPriceGroup(
    decimal UnitPrice,
    IReadOnlyList<string> InDocumentItems,
    IReadOnlyList<string> DashboardItems);

public partial class DuplicateUnitPriceCheckWindow : Window
{
    private readonly IReadOnlyList<DuplicateUnitPriceGroup> _groups;

    public DuplicateUnitPriceCheckWindow(IReadOnlyList<DuplicateUnitPriceGroup> groups)
    {
        InitializeComponent();
        _groups = groups;
        PopulateGroups();
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        MaxHeight = Math.Max(380, work.Height - 40);
        if (ActualHeight > MaxHeight)
        {
            SizeToContent = SizeToContent.Manual;
            Height = MaxHeight;
        }

        ContinueButton.BringIntoView();
    }

    private void PopulateGroups()
    {
        GroupsContainer.Children.Clear();
        var index = 1;

        foreach (var group in _groups.OrderBy(g => g.UnitPrice))
        {
            var groupBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12)
            };

            var groupPanel = new StackPanel();

            // Tiêu đề đơn giá N
            var headerText = new TextBlock
            {
                Text = $"Đơn giá {index}. {group.UnitPrice:N0} đ",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            groupPanel.Children.Add(headerText);

            // Các mặt hàng trong phiếu nhập có đơn giá này
            if (group.InDocumentItems.Count > 0)
            {
                var inDocPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                inDocPanel.Children.Add(new TextBlock
                {
                    Text = "• Mặt hàng trong phiếu nhập:",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7))
                });
                groupPanel.Children.Add(inDocPanel);

                foreach (var item in group.InDocumentItems)
                {
                    groupPanel.Children.Add(CreateItemRow(item));
                }
            }

            // Các mặt hàng trùng đơn giá với dashboard
            if (group.DashboardItems.Count > 0)
            {
                var dashPanel = new StackPanel();
                dashPanel.Children.Add(new TextBlock
                {
                    Text = "• Mặt hàng trùng đơn giá với Dashboard (danh mục hiện có):",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06))
                });
                groupPanel.Children.Add(dashPanel);

                foreach (var item in group.DashboardItems)
                {
                    groupPanel.Children.Add(CreateItemRow(item));
                }
            }

            groupBorder.Child = groupPanel;
            GroupsContainer.Children.Add(groupBorder);
            index++;
        }
    }

    private static UIElement CreateItemRow(string itemText)
    {
        var grid = new Grid { Margin = new Thickness(12, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBlock = new TextBlock
        {
            Text = $"• {itemText}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(textBlock, 0);
        grid.Children.Add(textBlock);

        var copyBtn = new Button
        {
            Content = "📋 Copy",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            BorderThickness = new Thickness(1),
            ToolTip = "Sao chép tên mặt hàng vào Clipboard"
        };
        copyBtn.Click += (_, _) =>
        {
            try
            {
                var textToCopy = ExtractItemName(itemText);
                Clipboard.SetText(textToCopy);
                copyBtn.Content = "✓ Đã copy";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (_, _) =>
                {
                    copyBtn.Content = "📋 Copy";
                    timer.Stop();
                };
                timer.Start();
            }
            catch
            {
                // Bỏ qua lỗi Clipboard nếu có
            }
        };

        Grid.SetColumn(copyBtn, 1);
        grid.Children.Add(copyBtn);

        return grid;
    }

    private static string ExtractItemName(string itemText)
    {
        if (string.IsNullOrWhiteSpace(itemText))
        {
            return string.Empty;
        }

        var text = itemText.Trim();

        // 1. Nếu là dòng thuộc phiếu nhập dạng "Dòng 1: Tên mặt hàng"
        var colonIndex = text.IndexOf(':');
        if (colonIndex >= 0 && text.StartsWith("Dòng ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[(colonIndex + 1)..].Trim();
        }

        // 2. Nếu là dòng thuộc Dashboard dạng "Tên mặt hàng (Mã: ..., Danh mục: ...)"
        var parenIndex = text.IndexOf(" (Mã:", StringComparison.OrdinalIgnoreCase);
        if (parenIndex >= 0)
        {
            text = text[..parenIndex].Trim();
        }

        return text;
    }

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
