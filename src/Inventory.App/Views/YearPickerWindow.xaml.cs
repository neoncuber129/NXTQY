using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace Inventory.App.Views;

public partial class YearPickerWindow : Window
{
    private static readonly Regex DigitsOnly = new(@"^\d+$", RegexOptions.Compiled);

    public int SelectedYear { get; private set; }

    public YearPickerWindow(int currentYear)
    {
        InitializeComponent();
        SelectedYear = currentYear;
        YearBox.Text = currentYear.ToString();
        YearBox.SelectAll();
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void YearBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    private void YearBox_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var text = (string)e.DataObject.GetData(typeof(string))!;
        if (!DigitsOnly.IsMatch(text))
        {
            e.CancelCommand();
        }
    }

    private void Confirm()
    {
        if (!int.TryParse(YearBox.Text.Trim(), out var year) || year < 1900 || year > 2100)
        {
            MessageBox.Show(this, "Vui lòng nhập năm hợp lệ (1900–2100).", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Information);
            YearBox.Focus();
            YearBox.SelectAll();
            return;
        }

        SelectedYear = year;
        DialogResult = true;
    }
}
