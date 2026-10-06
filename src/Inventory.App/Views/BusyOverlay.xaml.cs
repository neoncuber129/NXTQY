using System.Windows;
using System.Windows.Controls;

namespace Inventory.App.Views;

public partial class BusyOverlay : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(
            nameof(Message),
            typeof(string),
            typeof(BusyOverlay),
            new PropertyMetadata("Đang xử lý..."));

    public BusyOverlay() => InitializeComponent();

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
