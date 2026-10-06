using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Inventory.App.Helpers;

/// <summary>
/// Gán icon cửa sổ + taskbar từ resource nhúng / icon của file exe
/// (không cần copy app.ico / app-icon.png ra thư mục publish).
/// </summary>
internal static class AppIconHelper
{
    private const uint WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);
    private static readonly Uri PngPackUri = new("pack://application:,,,/Assets/app-icon.png", UriKind.Absolute);
    private static readonly Uri IcoPackUri = new("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
    private static readonly Dictionary<Window, (Drawing.Icon Big, Drawing.Icon Small)> HeldIcons = new();

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        TrySetWpfIcon(window);

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            TrySetWin32Icons(window);
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            TrySetWin32Icons(window);
        }
        else
        {
            window.SourceInitialized += OnSourceInitialized;
        }
    }

    private static void TrySetWpfIcon(Window window)
    {
        try
        {
            window.Icon = BitmapFrame.Create(
                PngPackUri,
                BitmapCreateOptions.None,
                BitmapCacheOption.OnLoad);
        }
        catch
        {
            try
            {
                var decoder = new IconBitmapDecoder(
                    IcoPackUri,
                    BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad);
                window.Icon = decoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .First();
            }
            catch
            {
                // Thử Win32 bên dưới.
            }
        }
    }

    private static void TrySetWin32Icons(Window window)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return;
            }

            using var extracted = Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (extracted is null)
            {
                return;
            }

            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            if (HeldIcons.Remove(window, out var old))
            {
                old.Big.Dispose();
                old.Small.Dispose();
            }

            var big = new Drawing.Icon(extracted, 256, 256);
            var small = new Drawing.Icon(extracted, 16, 16);
            HeldIcons[window] = (big, small);

            SendMessage(hwnd, WmSetIcon, IconBig, big.Handle);
            SendMessage(hwnd, WmSetIcon, IconSmall, small.Handle);

            window.Closed -= OnWindowClosed;
            window.Closed += OnWindowClosed;
        }
        catch
        {
            // Không chặn khởi động app.
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is Window window && HeldIcons.Remove(window, out var bag))
        {
            bag.Big.Dispose();
            bag.Small.Dispose();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
