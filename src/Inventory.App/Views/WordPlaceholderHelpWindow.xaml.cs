using System.Windows;
using Inventory.Reporting.Services;

namespace Inventory.App.Views;

public partial class WordPlaceholderHelpWindow : Window
{
    public WordPlaceholderHelpWindow()
    {
        InitializeComponent();
        ContentBox.Text = WordTemplateBuilder.BuildPlaceholderReferenceText();
    }

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ContentBox.Text);
        MessageBox.Show(this, "Đã sao chép danh sách placeholder.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
