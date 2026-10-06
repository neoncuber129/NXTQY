using Inventory.App.Helpers;
using Inventory.Data;
using Inventory.Data.Services;
using Inventory.Reporting.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Inventory.App;

public partial class App : Application
{
    private readonly IHost _host;
    private static int _isShowingGlobalError;

    public App()
    {
        RegisterGlobalExceptionHandlers();

        var vietnamCulture = new CultureInfo("vi-VN");
        CultureInfo.DefaultThreadCurrentCulture = vietnamCulture;
        CultureInfo.DefaultThreadCurrentUICulture = vietnamCulture;
        UiScaleManager.Initialize();

        // Lưu DB cạnh file exe (phù hợp bản publish dạng folder / portable).
        var dbPath = Path.Combine(AppContext.BaseDirectory, "inventory.db");
        Directory.CreateDirectory(AppContext.BaseDirectory);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContextFactory<InventoryDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));
                services.AddSingleton<InventoryService>();
                services.AddSingleton<ExcelImportService>();
                services.AddSingleton<ExcelTemplateService>();
                services.AddSingleton<DashboardExcelExportService>();
                services.AddSingleton<WordExportService>();
                services.AddSingleton<WordTemplateSettingsService>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // UI thread — không để exception làm tắt app.
        DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            ShowGlobalError(e.Exception);
        };

        // Thread khác (không phải UI).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                ShowGlobalError(ex);
            }
            else
            {
                ShowGlobalError(new Exception(e.ExceptionObject?.ToString() ?? "Lỗi không xác định."));
            }
        };

        // Task async bị bỏ sót (unobserved).
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            ShowGlobalError(e.Exception);
        };
    }

    private static void ShowGlobalError(Exception ex)
    {
        // Tránh vòng lặp nếu chính MessageBox / format lại lỗi.
        if (Interlocked.Exchange(ref _isShowingGlobalError, 1) == 1)
        {
            return;
        }

        try
        {
            var message = ExceptionMessageFormatter.Format(ex);
            void Show()
            {
                MessageBox.Show(
                    message,
                    "Đã xảy ra lỗi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var dispatcher = Current?.Dispatcher;
            if (dispatcher is null)
            {
                Show();
                return;
            }

            if (dispatcher.CheckAccess())
            {
                Show();
            }
            else
            {
                dispatcher.Invoke(Show, DispatcherPriority.Send);
            }
        }
        catch
        {
            // Không ném tiếp — mục tiêu là giữ app sống.
        }
        finally
        {
            Interlocked.Exchange(ref _isShowingGlobalError, 0);
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Inventory.App.Views.SplashWindow? splash = null;
        try
        {
            splash = new Inventory.App.Views.SplashWindow();
            AppIconHelper.Apply(splash);
            splash.Show();
            splash.UpdateProgress(15, "Đang kết nối cơ sở dữ liệu...", "Đang khởi tạo Host & Services...");

            await _host.StartAsync();

            splash.UpdateProgress(40, "Đang cập nhật cấu trúc cơ sở dữ liệu...", "Đang kiểm tra SQLite Database...");
            var inventoryService = _host.Services.GetRequiredService<InventoryService>();
            await inventoryService.InitializeAsync();

            splash.UpdateProgress(60, "Đang nạp dữ liệu ứng dụng...", "Vui lòng đợi trong giây lát...");
            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync((percent, status, subStatus) =>
                splash.UpdateProgress(percent, status, subStatus));

            splash.UpdateProgress(98, "Đang mở giao diện chính...", "Chuẩn bị hoàn tất...");
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;

            splash.UpdateProgress(100, "Sẵn sàng!", "Khởi động thành công.");
            await Task.Delay(180);

            window.Show();
            AppIconHelper.Apply(window);

            splash.Close();
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            splash?.Close();
            ShowGlobalError(ex);
            // Lỗi khởi tạo DB/host — không tiếp tục được thì mới thoát sau khi báo.
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        catch
        {
            // Bỏ qua lỗi khi thoát.
        }

        base.OnExit(e);
    }
}
