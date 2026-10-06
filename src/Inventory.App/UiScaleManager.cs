using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Inventory.App;

public static class UiScaleManager
{
    private sealed class WindowScaleState
    {
        public double AppliedScale { get; set; } = 1d;
        public Transform? OriginalTransform { get; init; }
    }

    private static readonly ConditionalWeakTable<Window, WindowScaleState> WindowStates = new();
    private static bool _isInitialized;
    private static double _scale = 1d;

    public static void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _scale = UiPreferenceService.LoadUiScale();
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Window_OnLoaded));
    }

    public static void SetScale(double scale)
    {
        if (scale is < 0.75d or > 1.75d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        _scale = scale;
        foreach (Window window in Application.Current.Windows)
        {
            ApplyScale(window);
        }
    }

    private static void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            ApplyScale(window);
        }
    }

    private static void ApplyScale(Window window)
    {
        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        var state = WindowStates.GetValue(
            window,
            _ => new WindowScaleState { OriginalTransform = content.LayoutTransform });
        if (Math.Abs(state.AppliedScale - _scale) < 0.001d)
        {
            return;
        }

        var ratio = _scale / state.AppliedScale;
        state.AppliedScale = _scale;
        content.LayoutTransform = Math.Abs(_scale - 1d) < 0.001d
            ? state.OriginalTransform
            : new ScaleTransform(_scale, _scale);

        var work = SystemParameters.WorkArea;
        if (!double.IsNaN(window.MinWidth) && !double.IsInfinity(window.MinWidth))
        {
            window.MinWidth = Math.Min(window.MinWidth * ratio, work.Width);
        }

        if (!double.IsNaN(window.MinHeight) && !double.IsInfinity(window.MinHeight))
        {
            window.MinHeight = Math.Min(window.MinHeight * ratio, work.Height);
        }

        // SizeToContent tự ôm nội dung (đã scale) — không gán Width/Height cứng kẻo cắt nút.
        var sizeToContent = window.SizeToContent;
        if (sizeToContent is SizeToContent.Manual or SizeToContent.Height)
        {
            if (!double.IsNaN(window.Width))
            {
                window.Width = Math.Min(window.Width * ratio, work.Width);
            }
        }

        if (sizeToContent is SizeToContent.Manual or SizeToContent.Width)
        {
            if (!double.IsNaN(window.Height))
            {
                window.Height = Math.Min(window.Height * ratio, work.Height);
            }
        }
        else if (sizeToContent is SizeToContent.WidthAndHeight or SizeToContent.Height)
        {
            window.MaxHeight = Math.Min(
                double.IsNaN(window.MaxHeight) || double.IsInfinity(window.MaxHeight)
                    ? work.Height - 40
                    : window.MaxHeight * ratio,
                work.Height - 40);
        }
    }
}
