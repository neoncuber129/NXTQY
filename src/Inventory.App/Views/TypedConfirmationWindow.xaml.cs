using System.Windows;
using System.Windows.Controls;

namespace Inventory.App.Views;

public partial class TypedConfirmationWindow : Window
{
    public TypedConfirmationWindow()
    {
        InitializeComponent();
    }

    public static bool IsConfirmationTextValid(string? text) =>
        string.Equals(text?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

    private void ConfirmationBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = IsConfirmationTextValid(ConfirmationBox.Text);
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (IsConfirmationTextValid(ConfirmationBox.Text))
        {
            DialogResult = true;
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
