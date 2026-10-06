using System.Windows;
using System.Windows.Threading;

namespace Inventory.App.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void UpdateProgress(double percent, string status, string? subStatus = null)
    {
        Dispatcher.Invoke(() =>
        {
            StartupProgressBar.Value = Math.Clamp(percent, 0, 100);
            PercentTextBlock.Text = $"{Math.Round(percent)}%";
            StatusTextBlock.Text = status;
            if (!string.IsNullOrWhiteSpace(subStatus))
            {
                SubStatusTextBlock.Text = subStatus;
            }

            // Force WPF UI to render immediately
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        });
    }
}
