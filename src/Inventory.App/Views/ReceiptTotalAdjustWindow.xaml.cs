using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Inventory.App.Helpers;

namespace Inventory.App.Views;

public partial class ReceiptTotalAdjustWindow : Window
{
    private readonly IReadOnlyList<PriceAdjustmentCalculator.InputLine> _lines;
    private readonly decimal _currentTotal;

    /// <summary>Phương án người dùng chọn áp dụng (null nếu đóng không áp dụng).</summary>
    public PriceAdjustmentSuggestion? SelectedSuggestion { get; private set; }

    public ReceiptTotalAdjustWindow(
        IReadOnlyList<PriceAdjustmentCalculator.InputLine> lines,
        decimal currentTotal,
        string title = "Điều chỉnh tổng tiền phiếu nhập")
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }
        _lines = lines;
        _currentTotal = currentTotal;
        CurrentTotalRun.Text = $"{currentTotal:N0} đ";
        TargetTotalBox.Focus();
    }

    private void TargetTotalBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Calculate_OnClick(sender, e);
        }
    }

    private void Calculate_OnClick(object sender, RoutedEventArgs e)
    {
        StatusText.Text = string.Empty;
        SuggestionsGrid.ItemsSource = null;

        var text = TargetTotalBox.Text?.Replace(".", string.Empty).Replace(",", string.Empty).Trim();
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var target) || target < 0)
        {
            StatusText.Text = "Tổng tiền đúng không hợp lệ.";
            return;
        }

        if (target == _currentTotal)
        {
            StatusText.Text = "Tổng nhập vào bằng tổng hiện tại — không cần chỉnh.";
            return;
        }

        var suggestions = PriceAdjustmentCalculator.Suggest(_lines, target);
        if (suggestions.Count == 0)
        {
            StatusText.Text = "Không tìm được phương án chỉnh đơn giá phù hợp.";
            return;
        }

        SuggestionsGrid.ItemsSource = suggestions.Select(s => new SuggestionRow(s)).ToList();
        SuggestionsGrid.SelectedIndex = 0;
        StatusText.Text = suggestions.Any(x => x.IsExact)
            ? string.Empty
            : "Không có phương án khớp tuyệt đối — các phương án dưới đây gần nhất.";
    }

    private void SuggestionsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        Apply_OnClick(sender, e);

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (SuggestionsGrid.SelectedItem is not SuggestionRow row)
        {
            StatusText.Text = "Hãy chọn một phương án trước.";
            return;
        }

        SelectedSuggestion = row.Suggestion;
        DialogResult = true;
    }

    private sealed class SuggestionRow(PriceAdjustmentSuggestion suggestion)
    {
        public PriceAdjustmentSuggestion Suggestion { get; } = suggestion;
        public string Summary => Suggestion.Summary;
        public decimal ResultingTotal => Suggestion.ResultingTotal;
        public string ResidualDisplay => Suggestion.IsExact ? "Khớp đúng" : $"{Suggestion.Residual:N0}";
        public string Detail => string.Join("; ", Suggestion.Changes.Select(c =>
            $"Dòng {c.RowNumber} ({Shorten(c.Label)}): {FormatMoney(c.OldPrice)} → {FormatMoney(c.NewPrice)} ({(c.PriceDelta >= 0 ? "+" : "")}{FormatMoney(c.PriceDelta)})"));

        private static string FormatMoney(decimal value) =>
            value % 1 == 0 ? $"{value:N0}" : $"{value:#,##0.##}";

        private static string Shorten(string label) =>
            label.Length <= 30 ? label : label[..27] + "...";
    }
}
