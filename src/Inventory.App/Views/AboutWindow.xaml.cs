using System.Reflection;
using System.Windows;

namespace Inventory.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText = version is null
            ? "Phiên bản 1.1"
            : $"Phiên bản {version.Major}.{version.Minor}";
        DataContext = this;
    }

    public string VersionText { get; }
}
