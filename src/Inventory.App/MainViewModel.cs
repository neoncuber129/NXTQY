using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inventory.App.Helpers;
using Inventory.App.Views;
using Inventory.Data.Services;
using Inventory.Domain.Models;
using Inventory.Domain.Services;
using Inventory.Reporting.Services;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace Inventory.App;

[Flags]
internal enum LiveDataScope
{
    None = 0,
    Items = 1,
    Receipts = 2,
    Issues = 4,
    Dashboard = 8,
    OpeningBalances = 16,
    VouchersAndDashboard = Receipts | Issues | Dashboard,
    CatalogAndDashboard = Items | Dashboard,
    YearView = Receipts | Issues | Dashboard | OpeningBalances,
    All = Items | Receipts | Issues | Dashboard | OpeningBalances
}

public sealed record UsageTypePickItem(string Display, string Value);

public sealed record CategoryOption(string Label, string Value);

public sealed record UnitReportUnitOption(int? UnitId, string Display);

public sealed partial class UnitSummaryUnitOption : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool isSelected = true;
    public int UnitId { get; }
    public string UnitCode { get; }
    public string UnitName { get; }
    /// <summary>Hiển thị trên list: "Code - Name" hoặc "Name" nếu không có code.</summary>
    public string UnitDisplay { get; }
    public UnitSummaryUnitOption(int unitId, string unitCode, string unitName)
    {
        UnitId = unitId;
        UnitCode = unitCode;
        UnitName = unitName;
        UnitDisplay = string.IsNullOrWhiteSpace(unitCode) ? unitName : $"{unitCode} - {unitName}";
    }
}

public partial class MainViewModel(
    InventoryService inventoryService,
    ExcelImportService excelImportService,
    ExcelTemplateService excelTemplateService,
    DashboardExcelExportService dashboardExcelExportService,
    WordExportService wordExportService,
    WordTemplateSettingsService wordTemplateSettingsService) : ObservableObject
{
    private bool _isLiveSyncing;
    private readonly InventoryExcelExportService _inventoryExcelExportService = new();
    private static readonly DashboardFilterColumnId[] AllDashboardFilterColumns =
        Enum.GetValues<DashboardFilterColumnId>();

    public ObservableCollection<Item> Items { get; } = [];
    public ObservableCollection<Receipt> Receipts { get; } = [];
    public ObservableCollection<Issue> Issues { get; } = [];
    public ObservableCollection<SubordinateUnit> SubordinateUnits { get; } = [];
    public ObservableCollection<OpeningBalanceInputViewModel> OpeningBalanceLines { get; } = [];
    public BulkObservableCollection<DashboardRowViewModel> DashboardRows { get; } = [];
    public ObservableCollection<ManagedUnitIssueRow> ManagedUnitIssueRows { get; } = [];
    public ObservableCollection<UnitReportUnitOption> UnitReportUnitOptions { get; } = [];
    private readonly Dictionary<string, OpeningBalanceInput> _removedOpeningBalanceLines =
        new(StringComparer.OrdinalIgnoreCase);
    public Func<bool>? OpeningBalanceConfirmation { get; set; }

    public const string DashboardFilterEmptyLabel = "(Trống)";

    public ObservableCollection<CategoryOption> DashboardCategoryOptions { get; } = [];

    public ObservableCollection<UsageTypePickItem> DashboardUsageTypePickList { get; } =
    [
        new UsageTypePickItem("Tự mua sắm", string.Empty),
        new UsageTypePickItem("Trên cấp", "trên cấp")
    ];

    [ObservableProperty] private int selectedMainTabIndex;
    [ObservableProperty] private int selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string itemCategory = string.Empty;
    [ObservableProperty] private string itemUnit = string.Empty;
    [ObservableProperty] private string itemUsageType = string.Empty;
    [ObservableProperty] private string itemUnitPrice = "0";
    [ObservableProperty] private Receipt? selectedReceipt;
    [ObservableProperty] private Issue? selectedIssue;
    [ObservableProperty] private string statusMessage = "Sẵn sàng";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string busyMessage = "Đang xử lý...";
    [ObservableProperty] private decimal dashboardTotalAmount;
    [ObservableProperty] private bool dashboardEditUnlocked;
    [ObservableProperty] private bool isModeB = UiPreferenceService.LoadIsModeB();
    [ObservableProperty] private string dashboardSearchText = string.Empty;

    partial void OnIsModeBChanged(bool value)
    {
        UiPreferenceService.SaveIsModeB(value);
    }

    [ObservableProperty] private bool omitZeroStockAtIssueDate = UiPreferenceService.LoadOmitZeroStockAtIssueDate();

    partial void OnOmitZeroStockAtIssueDateChanged(bool value)
    {
        UiPreferenceService.SaveOmitZeroStockAtIssueDate(value);
    }
    [ObservableProperty] private string wordTemplateFolderPath = string.Empty;
    [ObservableProperty] private string receiptWordTemplatePath = string.Empty;
    [ObservableProperty] private string issueWordTemplatePath = string.Empty;
    [ObservableProperty] private string balanceWordTemplatePath = string.Empty;

    public string WordTemplateFolderDisplay =>
        string.IsNullOrWhiteSpace(WordTemplateFolderPath)
            ? "Thư mục hiện tại: (chưa chọn)"
            : $"Thư mục hiện tại: {WordTemplateFolderPath}";

    partial void OnWordTemplateFolderPathChanged(string value) =>
        OnPropertyChanged(nameof(WordTemplateFolderDisplay));

    [ObservableProperty] private SubordinateUnit? selectedSubordinateUnit;
    [ObservableProperty] private DateTime managedUnitIssueFromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime managedUnitIssueToDate = new(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private UnitReportUnitOption? selectedManagedUnitIssueUnit;
    [ObservableProperty] private string managedUnitIssueKeyword = string.Empty;
    [ObservableProperty] private bool isManagedUnitIssueLoading;
    [ObservableProperty] private ManagedUnitIssueRow? selectedManagedUnitIssue;

    // --- Tab Tổng hợp ---
    [ObservableProperty] private DateTime summarySummaryFromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime summarySummaryToDate = new(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private bool summarySeparateSheets;
    /// <summary>true = dùng mã đơn vị làm nhãn cột/sheet; false = dùng tên đơn vị.</summary>
    [ObservableProperty] private bool summaryLabelByCode = true;
    public ObservableCollection<UnitSummaryUnitOption> SummaryUnitOptions { get; } = [];

    private CollectionViewSource? _dashboardViewSource;
    private string _dashboardSearchNormalized = string.Empty;
    private DispatcherTimer? _dashboardSearchDebounceTimer;

    private readonly Dictionary<DashboardFilterColumnId, HashSet<string>?> _dashboardColumnIncluded =
        new()
        {
            [DashboardFilterColumnId.ItemCode] = null,
            [DashboardFilterColumnId.Category] = null,
            [DashboardFilterColumnId.ItemName] = null,
            [DashboardFilterColumnId.Unit] = null,
            [DashboardFilterColumnId.UnitPrice] = null,
            [DashboardFilterColumnId.UsageType] = null,
            [DashboardFilterColumnId.OpeningQuantity] = null,
            [DashboardFilterColumnId.ReceivedQuantity] = null,
            [DashboardFilterColumnId.IssuedQuantity] = null,
            [DashboardFilterColumnId.ClosingQuantity] = null,
            [DashboardFilterColumnId.Amount] = null,
            [DashboardFilterColumnId.Note] = null
        };

    public ICollectionView DashboardRowsView => EnsureDashboardView().View;
    public string ManagedUnitIssueSelectedCountText =>
        $"Đã chọn: {ManagedUnitIssueRows.Count(x => x.IsSelected)}/{ManagedUnitIssueRows.Count}";
    public bool HasUnsavedManagedUnitIssues => ManagedUnitIssueRows.Any(x => x.IsDirty);

    private bool _isInitialized;

    public async Task InitializeAsync(Action<double, string, string?>? progressCallback = null)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        progressCallback?.Invoke(70, "Đang nạp cấu hình mẫu Word...", "Đang đọc thiết lập...");
        var settings = wordTemplateSettingsService.Load();
        WordTemplateFolderPath = settings.TemplateFolderPath;
        ReceiptWordTemplatePath = settings.ReceiptTemplatePath;
        IssueWordTemplatePath = settings.IssueTemplatePath;
        BalanceWordTemplatePath = settings.BalanceTemplatePath;

        progressCallback?.Invoke(78, "Đang nạp danh sách đơn vị...", "Đang tải danh mục đơn vị...");
        await RefreshSubordinateUnitsAsync();

        progressCallback?.Invoke(85, "Đang nạp dữ liệu sổ kho & tồn kho...", "Đang xử lý dữ liệu...");
        await SyncLiveDataAsync(LiveDataScope.All);

        progressCallback?.Invoke(95, "Đang khởi tạo phiếu xuất đơn vị...", "Chuẩn bị hoàn tất...");
        ResetManagedUnitIssuePeriod(SelectedYear);
        await RefreshManagedUnitIssuesAsync();
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        try
        {
            if (!await ConfirmDiscardPendingEditsAsync("thêm mặt hàng", dashboard: true, opening: false, units: false))
            {
                return;
            }

            if (!decimal.TryParse(ItemUnitPrice, out var price) || price < 0)
            {
                throw new InvalidOperationException("Đơn giá không hợp lệ (không được âm).");
            }

            var item = new Item
            {
                ItemCode = ItemCode.Trim(),
                ItemName = ItemName.Trim(),
                Category = ItemCategory.Trim(),
                Unit = ItemUnit.Trim(),
                UsageType = ItemUsageType.Trim(),
                UnitPrice = price
            };
            await inventoryService.AddItemAsync(item);
            SetStatusAfterSave($"Đã thêm mặt hàng: {item.ItemCode}.");
            ClearItemForm();
            await SyncLiveDataAsync(LiveDataScope.CatalogAndDashboard);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private void AddOpeningLine()
    {
        AddOpeningLineToCollection(new OpeningBalanceInputViewModel());
        NotifyMainDraftState();
    }

    [RelayCommand]
    private void RemoveSelectedOpeningLines()
    {
        var selected = OpeningBalanceLines.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowError("Hãy tick ít nhất một dòng tồn đầu cần xóa.");
            return;
        }

        foreach (var line in selected)
        {
            if (!string.IsNullOrWhiteSpace(line.ItemName))
            {
                var cleared = ToOpeningBalanceInput(line);
                cleared.OpeningQuantity = 0;
                _removedOpeningBalanceLines[GetOpeningBalanceKey(cleared)] = cleared;
            }

            line.PropertyChanged -= OpeningLine_OnPropertyChanged;
            OpeningBalanceLines.Remove(line);
        }

        StatusMessage = $"Đã xóa {selected.Count} dòng khỏi danh sách; bấm Lưu tồn đầu năm để ghi nhận.";
        NotifyMainDraftState();
    }

    [RelayCommand]
    private async Task SaveOpeningBalancesAsync()
    {
        try
        {
            if (!HasUnsavedOpeningBalances)
            {
                StatusMessage = "Tồn đầu năm không có thay đổi cần lưu.";
                return;
            }

            var lines = BuildOpeningBalanceInputsForSave();

            if (!CheckDuplicateOpeningBalanceUnitPrices(lines))
            {
                StatusMessage = "Đã hủy lưu tồn đầu năm do phát hiện trùng đơn giá.";
                return;
            }

            if (!ConfirmOpeningBalanceTotalAmount())
            {
                StatusMessage = "Đã hủy lưu thay đổi tồn đầu năm.";
                return;
            }

            if (!ConfirmOpeningBalanceChanges())
            {
                StatusMessage = "Đã hủy lưu thay đổi tồn đầu năm.";
                return;
            }

            if (!await ResolveOpeningBalanceVariantsAsync(lines))
            {
                StatusMessage = "Đã hủy lưu tồn đầu năm (chưa chọn biến thể mặt hàng).";
                return;
            }

            var ok = await RunBusyAsync(
                "Đang lưu tồn đầu năm...",
                async () =>
                {
                    await inventoryService.UpsertOpeningBalancesAsync(SelectedYear, lines);
                    _removedOpeningBalanceLines.Clear();
                    CaptureOpeningBaseline();
                    await SyncLiveDataAsync(LiveDataScope.Items | LiveDataScope.Dashboard | LiveDataScope.OpeningBalances);
                });
            if (!ok)
            {
                return;
            }

            SetStatusAfterSave($"Đã cập nhật tồn đầu năm {SelectedYear}.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task CarryForwardAsync()
    {
        try
        {
            if (!await ConfirmDiscardPendingEditsAsync("chuyển kỳ"))
            {
                return;
            }

            var toYear = SelectedYear;
            var fromYear = toYear - 1;

            var fromActivity = await inventoryService.GetYearStockActivityAsync(fromYear);
            var toActivity = await inventoryService.GetYearStockActivityAsync(toYear);

            var dialog = new CarryForwardWindow(SelectedYear, fromActivity, toActivity)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var vm = dialog.ViewModel;
            if (vm.CreateBackup)
            {
                var folder = Path.GetDirectoryName(vm.BackupFilePath);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                await RunBusyAsync(
                    "Đang sao lưu dữ liệu trước khi chuyển kỳ...",
                    () => inventoryService.BackupDatabaseAsync(vm.BackupFilePath).GetAwaiter().GetResult());
                StatusMessage = $"Đã backup trước chuyển kỳ: {vm.BackupFilePath}";
            }

            await RunBusyAsync(
                $"Đang chuyển kỳ {vm.FromYear} → {vm.ToYear}...",
                async () =>
                {
                    await inventoryService.CarryForwardPeriodAsync(
                        vm.FromYear,
                        vm.ToYear,
                        overwriteExisting: true,
                        excludeZeroClosingStock: vm.ExcludeZeroClosingStock,
                        allowUpdateOpeningWithExistingActivity: vm.AllowUpdateOpeningWithExistingActivity);
                    await SyncLiveDataAsync(LiveDataScope.Dashboard | LiveDataScope.OpeningBalances);
                });
            var zeroStockNote = vm.ExcludeZeroClosingStock
                ? " (đã loại các mặt hàng tồn cuối = 0 khỏi Dashboard năm sau)"
                : string.Empty;
            SetStatusAfterSave(
                $"Đã chuyển kỳ [{vm.OperationName}] lúc {DateTime.Now:dd/MM/yyyy HH:mm:ss} " +
                $"(tồn cuối {vm.FromYear} → tồn đầu {vm.ToYear}){zeroStockNote}.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        try
        {
            if (!await ConfirmDiscardPendingEditsAsync("tải lại Dashboard", dashboard: true, opening: false, units: false))
            {
                return;
            }

            await RunBusyAsync(
                "Đang tải lại Dashboard...",
                async () => await RefreshDashboardCoreAsync(updateStatusMessage: true));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ImportOpeningExcelAsync()
    {
        var file = PickExcelFile();
        if (file is null)
        {
            return;
        }

        try
        {
            ImportResult<OpeningBalanceInput>? result = null;
            var ok = await RunBusyAsync(
                "Đang import Excel tồn đầu năm...",
                () => result = excelImportService.ImportOpeningBalances(file));
            if (!ok || result is null)
            {
                return;
            }

            if (!result.IsValid)
            {
                ShowError(string.Join(Environment.NewLine, result.Errors.Select(x => $"Dong {x.RowNumber}: {x.Message}")));
                return;
            }

            ClearOpeningBalanceLines();
            _removedOpeningBalanceLines.Clear();
            foreach (var line in result.Data)
            {
                AddOpeningLineToCollection(new OpeningBalanceInputViewModel(line));
            }

            // Import chưa ghi DB — giữ dirty để nhắc lưu khi thoát.
            StatusMessage = $"Đã import {result.Data.Count} dòng tồn đầu năm (chưa lưu).";
            NotifyMainDraftState();
        }
        catch (Exception ex)
        {
            ShowFileWriteError(ex, file);
        }
    }

    [RelayCommand]
    private async Task SaveReceiptTemplateAsync()
    {
        var output = SaveExcelFile("Template_PhieuNhap.xlsx");
        if (output is null)
        {
            return;
        }

        try
        {
            await RunBusyAsync(
                "Đang tạo template Excel phiếu nhập...",
                () => excelTemplateService.CreateReceiptTemplate(output));
            StatusMessage = $"Đã tạo template: {output}";
        }
        catch (Exception ex)
        {
            ShowFileWriteError(ex, output);
        }
    }

    [RelayCommand]
    private async Task SaveOpeningTemplateAsync()
    {
        var output = SaveExcelFile("Template_TonDauNam.xlsx");
        if (output is null)
        {
            return;
        }

        try
        {
            await RunBusyAsync(
                "Đang tạo template Excel tồn đầu năm...",
                () => excelTemplateService.CreateOpeningTemplate(output));
            StatusMessage = $"Đã tạo template: {output}";
        }
        catch (Exception ex)
        {
            ShowFileWriteError(ex, output);
        }
    }

    [RelayCommand]
    private void ConfigureWordTemplateFolder()
    {
        var folder = PickFolder("Chọn thư mục mặc định chứa template Word");
        if (folder is null)
        {
            return;
        }

        ApplyWordTemplateFolder(folder);
        wordTemplateSettingsService.SaveFolder(folder);

        var missingFiles = new[]
            {
                ReceiptWordTemplatePath,
                IssueWordTemplatePath,
                BalanceWordTemplatePath
            }
            .Where(path => !File.Exists(path))
            .Select(Path.GetFileName)
            .ToList();
        StatusMessage = missingFiles.Count == 0
            ? $"Đã chọn thư mục template Word: {folder}"
            : $"Đã chọn thư mục template; còn thiếu: {string.Join(", ", missingFiles)}";
    }

    private void ApplyWordTemplateFolder(string folder)
    {
        WordTemplateFolderPath = folder;
        ReceiptWordTemplatePath = Path.Combine(
            folder,
            WordTemplateSettingsService.ReceiptTemplateFileName);
        IssueWordTemplatePath = Path.Combine(
            folder,
            WordTemplateSettingsService.IssueTemplateFileName);
        BalanceWordTemplatePath = Path.Combine(
            folder,
            WordTemplateSettingsService.BalanceTemplateFileName);
    }

    [RelayCommand]
    private void ShowWordPlaceholderHelp()
    {
        var window = new WordPlaceholderHelpWindow
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    [RelayCommand]
    private async Task ExportDashboardExcelAsync()
    {
        var output = SaveExcelFile($"Dashboard_{SelectedYear}.xlsx");
        if (output is null)
        {
            return;
        }

        var rows = DashboardRowsView
            .Cast<object>()
            .OfType<DashboardRowViewModel>()
            .Select(row => new DashboardExcelExportRow(
                row.ItemCode,
                row.Category,
                row.ItemName,
                row.Unit,
                row.UnitPrice,
                DashboardRowViewModel.FormatUsageTypeDisplay(row.UsageType),
                row.OpeningQuantity,
                row.ReceivedQuantity,
                row.IssuedQuantity,
                row.ClosingQuantity,
                row.Amount,
                row.Note))
            .ToList();

        try
        {
            await RunBusyAsync(
                "Đang xuất Excel Dashboard...",
                () => dashboardExcelExportService.Export(output, rows, SelectedYear));
            StatusMessage = $"Đã xuất Excel dashboard: {output}";
        }
        catch (Exception ex)
        {
            ShowFileWriteError(ex, output);
        }
    }

    [RelayCommand]
    private async Task RefreshReceiptsAsync()
    {
        var receipts = await inventoryService.GetReceiptsAsync(SelectedYear);
        Receipts.Clear();
        foreach (var receipt in receipts)
        {
            Receipts.Add(receipt);
        }
    }

    [RelayCommand]
    private async Task RefreshIssuesAsync()
    {
        var issues = await inventoryService.GetIssuesAsync(SelectedYear);
        Issues.Clear();
        foreach (var issue in issues)
        {
            Issues.Add(issue);
        }
    }

    [RelayCommand]
    private async Task RefreshManagedUnitIssuesAsync()
    {
        if (HasUnsavedManagedUnitIssues)
        {
            var discard = MessageBox.Show(
                "Danh sách phiếu xuất đơn vị có thay đổi chưa lưu.\n\nBỏ thay đổi và tải lại?",
                "Thay đổi chưa lưu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (discard != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await LoadManagedUnitIssuesAsync(updateStatus: true);
    }

    private async Task LoadManagedUnitIssuesAsync(bool updateStatus)
    {
        if (IsManagedUnitIssueLoading)
        {
            return;
        }

        try
        {
            if (ManagedUnitIssueToDate.Date < ManagedUnitIssueFromDate.Date)
            {
                throw new InvalidOperationException("Ngày kết thúc phải từ ngày bắt đầu trở đi.");
            }

            IsManagedUnitIssueLoading = true;
            var rows = await inventoryService.GetUnitIssueReportAsync(
                ManagedUnitIssueFromDate,
                ManagedUnitIssueToDate,
                SelectedManagedUnitIssueUnit?.UnitId,
                ManagedUnitIssueKeyword);
            foreach (var oldRow in ManagedUnitIssueRows)
            {
                oldRow.PropertyChanged -= OnManagedUnitIssueRowPropertyChanged;
            }

            ManagedUnitIssueRows.Clear();
            foreach (var source in rows)
            {
                var row = new ManagedUnitIssueRow(source);
                row.PropertyChanged += OnManagedUnitIssueRowPropertyChanged;
                ManagedUnitIssueRows.Add(row);
            }

            SelectedManagedUnitIssue = null;
            NotifyManagedUnitIssueState();
            if (updateStatus)
            {
                StatusMessage = $"Đã tải {rows.Count} phiếu xuất đơn vị.";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsManagedUnitIssueLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveManagedUnitIssuesAsync()
    {
        try
        {
            var dirtyRows = ManagedUnitIssueRows.Where(x => x.IsDirty).ToList();
            if (dirtyRows.Count == 0)
            {
                StatusMessage = "Không có thay đổi phiếu xuất đơn vị cần lưu.";
                return;
            }

            var patches = dirtyRows.Select(x => x.ToHeaderPatch()).ToList();
            await inventoryService.UpdateUnitIssueHeadersAsync(patches);
            await LoadManagedUnitIssuesAsync(updateStatus: false);
            SetStatusAfterSave($"Đã lưu {patches.Count} phiếu xuất đơn vị.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task DiscardManagedUnitIssueChangesAsync()
    {
        await LoadManagedUnitIssuesAsync(updateStatus: false);
        StatusMessage = "Đã bỏ các thay đổi phiếu xuất đơn vị.";
    }

    [RelayCommand]
    private async Task PreviewManagedUnitIssueAsync(ManagedUnitIssueRow? row)
    {
        try
        {
            EnsureManagedUnitIssuesSaved();
            row ??= SelectedManagedUnitIssue;
            if (row is null)
            {
                throw new InvalidOperationException("Vui lòng chọn phiếu xuất cần xem.");
            }

            var issue = (await inventoryService.GetUnitIssuesByIdsAsync([row.IssueId])).SingleOrDefault()
                ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu xuất.");
            var window = new VoucherPreviewWindow(
                wordExportService,
                issue,
                Items.ToDictionary(x => x.Id),
                IssueWordTemplatePath,
                SubordinateUnits.ToList(),
                showWordExportButton: false)
            {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ExportManagedUnitIssuesWordAsync()
    {
        try
        {
            EnsureManagedUnitIssuesSaved();
            var ids = ManagedUnitIssueRows.Where(x => x.IsSelected).Select(x => x.IssueId).ToList();
            if (ids.Count == 0)
            {
                throw new InvalidOperationException("Hãy tick ít nhất một phiếu xuất để xuất Word.");
            }

            if (string.IsNullOrWhiteSpace(IssueWordTemplatePath) || !File.Exists(IssueWordTemplatePath))
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy {WordTemplateSettingsService.IssueTemplateFileName}.\n" +
                    "Vào Cài đặt → Template Word để chọn thư mục hoặc tạo file mẫu.");
            }

            var issues = await inventoryService.GetUnitIssuesByIdsAsync(ids);
            var choice = MessageBox.Show(
                "Chọn Yes để xuất gộp vào một file Word.\n" +
                "Chọn No để xuất mỗi phiếu thành một file riêng.",
                "Cách xuất phiếu Word",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            var itemsById = Items.ToDictionary(x => x.Id);
            if (choice == MessageBoxResult.Yes)
            {
                var output = SaveWordFile(
                    issues.Count == 1
                        ? $"PX_{SafeFileComponent(issues[0].IssueNumber)}.docx"
                        : $"PX_DonVi_Gop_{issues.Count}phieu.docx");
                if (output is null)
                {
                    return;
                }

                await RunBusyAsync(
                    $"Đang xuất gộp {issues.Count} phiếu Word...",
                    () => wordExportService.ExportIssuesCombined(IssueWordTemplatePath, output, issues, itemsById));
                StatusMessage = $"Đã xuất gộp {issues.Count} phiếu Word: {output}";
                return;
            }

            var folder = PickFolder("Chọn thư mục lưu các phiếu xuất theo đơn vị");
            if (folder is null)
            {
                return;
            }

            await RunBusyAsync(
                $"Đang xuất {issues.Count} phiếu Word...",
                () =>
                {
                    foreach (var issue in issues)
                    {
                        var output = Path.Combine(
                            folder,
                            $"PX_{SafeFileComponent(issue.IssueNumber)}_{SafeFileComponent(issue.ReceivedUnit)}.docx");
                        wordExportService.ExportIssue(IssueWordTemplatePath, output, issue, itemsById);
                    }
                });

            StatusMessage = $"Đã xuất tách {issues.Count} phiếu Word vào: {folder}";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public void SelectAllManagedUnitIssues(bool selected)
    {
        foreach (var row in ManagedUnitIssueRows)
        {
            row.IsSelected = selected;
        }
    }

    public void SelectAllReceipts(bool selected)
    {
        foreach (var receipt in Receipts)
        {
            receipt.IsSelected = selected;
        }
    }

    public void SelectAllIssues(bool selected)
    {
        foreach (var issue in Issues)
        {
            issue.IsSelected = selected;
        }
    }

    private void OnManagedUnitIssueRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManagedUnitIssueRow.IsDirty) or nameof(ManagedUnitIssueRow.IsSelected))
        {
            NotifyManagedUnitIssueState();
        }
    }

    private void NotifyManagedUnitIssueState()
    {
        OnPropertyChanged(nameof(HasUnsavedManagedUnitIssues));
        OnPropertyChanged(nameof(ManagedUnitIssueSelectedCountText));
        OnPropertyChanged(nameof(HasUnsavedMainEdits));
    }

    private void EnsureManagedUnitIssuesSaved()
    {
        if (HasUnsavedManagedUnitIssues)
        {
            throw new InvalidOperationException(
                "Có thay đổi phiếu xuất đơn vị chưa lưu. Hãy bấm Lưu thay đổi trước khi xem hoặc xuất Word.");
        }
    }

    [RelayCommand]
    private void SelectAllSummaryUnits()
    {
        foreach (var u in SummaryUnitOptions) u.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAllSummaryUnits()
    {
        foreach (var u in SummaryUnitOptions) u.IsSelected = false;
    }

    [RelayCommand]
    private async Task ExportUnitSummaryAsync()
    {
        try
        {
            if (SummarySummaryToDate.Date < SummarySummaryFromDate.Date)
            {
                throw new InvalidOperationException("Ngày kết thúc phải từ ngày bắt đầu trở đi.");
            }

            var selectedUnits = SummaryUnitOptions.Where(x => x.IsSelected).ToList();
            if (selectedUnits.Count == 0)
            {
                throw new InvalidOperationException("Hãy chọn ít nhất một đơn vị để tổng hợp.");
            }

            var output = SaveExcelFile(
                $"TongHop_{SummarySummaryFromDate:yyyyMMdd}_{SummarySummaryToDate:yyyyMMdd}.xlsx");
            if (output is null)
            {
                return;
            }

            var selectedUnitIds = selectedUnits.Select(x => x.UnitId).ToList();
            List<Inventory.Domain.Models.UnitIssueReportLine> lines = [];

            var ok = await RunBusyAsync(
                "Đang tổng hợp dữ liệu xuất theo đơn vị...",
                async () =>
                {
                    var rows = await inventoryService.GetUnitIssueReportAsync(
                        SummarySummaryFromDate,
                        SummarySummaryToDate);
                    var filteredIds = rows
                        .Where(r => r.SubordinateUnitId.HasValue
                                    && selectedUnitIds.Contains(r.SubordinateUnitId!.Value))
                        .Select(r => r.IssueId)
                        .ToList();
                    lines = filteredIds.Count > 0
                        ? await inventoryService.GetUnitIssueReportLinesAsync(filteredIds)
                        : [];
                });

            if (!ok)
            {
                return;
            }

            if (lines.Count == 0)
            {
                StatusMessage = "Không có dữ liệu xuất trong khoảng thời gian và đơn vị đã chọn.";
                return;
            }

            var unitList = selectedUnits
                .Select(u =>
                {
                    // Label dùng cho tên cột/sheet trong Excel
                    var label = SummaryLabelByCode && !string.IsNullOrWhiteSpace(u.UnitCode)
                        ? u.UnitCode
                        : u.UnitName;
                    return (u.UnitId, u.UnitCode, u.UnitName, label);
                })
                .ToList();

            await RunBusyAsync(
                "Đang xuất Excel tổng hợp...",
                () => _inventoryExcelExportService.ExportUnitSummary(
                    output,
                    lines,
                    unitList,
                    SummarySummaryFromDate,
                    SummarySummaryToDate));

            StatusMessage = $"Đã xuất Excel tổng hợp ({selectedUnits.Count} đơn vị): {output}";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"NXT_{SelectedYear}_{DateTime.Now:yyyyMMdd_HHmmss}.db",
            Filter = "SQLite Database (*.db)|*.db"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await RunBusyAsync(
                "Đang sao lưu dữ liệu...",
                () => inventoryService.BackupDatabaseAsync(dialog.FileName).GetAwaiter().GetResult());
            StatusMessage = $"Đã backup dữ liệu (năm gắn nhãn {SelectedYear}): {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task RestoreDatabaseAsync()
    {
        var openDialog = new OpenFileDialog
        {
            Filter = "SQLite Database (*.db)|*.db",
            CheckFileExists = true
        };
        if (openDialog.ShowDialog() != true)
        {
            return;
        }

        var confirm = MessageBox.Show(
            "Restore sẽ ghi đè toàn bộ dữ liệu hiện tại. Bạn có chắc chắn muốn tiếp tục?",
            "Xác nhận restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await ConfirmDiscardPendingEditsAsync("restore dữ liệu"))
        {
            return;
        }

        try
        {
            await RunBusyAsync(
                "Đang restore dữ liệu...",
                async () =>
                {
                    await inventoryService.RestoreDatabaseAsync(openDialog.FileName);
                    await inventoryService.InitializeAsync();
                    await ReloadAllDataAsync();
                });
            StatusMessage = $"Đã restore dữ liệu từ: {openDialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowFileWriteError(ex, openDialog.FileName);
        }
    }

    [RelayCommand]
    private async Task ClearAllDataAsync()
    {
        var confirm = MessageBox.Show(
            "Thao tác này sẽ xóa trắng dữ liệu như khi mới cài phần mềm:\n" +
            "mặt hàng, toàn bộ phiếu, tồn kho, đơn vị trực thuộc và cài đặt cá nhân.\n\n" +
            "Các file template Word nằm ngoài ứng dụng sẽ không bị xóa.\n\n" +
            "Bạn có chắc chắn?",
            "Xác nhận xóa toàn bộ dữ liệu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await ConfirmDiscardPendingEditsAsync("xóa toàn bộ dữ liệu"))
        {
            return;
        }

        try
        {
            await RunBusyAsync(
                "Đang xóa toàn bộ dữ liệu...",
                async () =>
                {
                    await inventoryService.ClearAllDataAsync();
                    wordTemplateSettingsService.Reset();
                    UiPreferenceService.Reset();
                    WordTemplateFolderPath = string.Empty;
                    ReceiptWordTemplatePath = string.Empty;
                    IssueWordTemplatePath = string.Empty;
                    BalanceWordTemplatePath = string.Empty;
                    await ReloadAllDataAsync();
                });
            SetStatusAfterSave("Đã xóa trắng toàn bộ dữ liệu và cài đặt cá nhân.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task AddSubordinateUnitAsync()
    {
        try
        {
            if (!await ConfirmDiscardPendingEditsAsync(
                    "thêm đơn vị", dashboard: false, opening: false, units: true))
            {
                return;
            }

            var dialog = new SubordinateUnitDialogWindow
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var unit = await inventoryService.AddSubordinateUnitAsync(dialog.Unit);
            SetStatusAfterSave($"Đã thêm đơn vị trực thuộc: {unit.Name}.");
            await RefreshSubordinateUnitsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task SaveSubordinateUnitsAsync()
    {
        try
        {
            await inventoryService.SaveSubordinateUnitsAsync(SubordinateUnits.ToList());
            SetStatusAfterSave("Đã lưu thông tin đơn vị trực thuộc.");
            await RefreshSubordinateUnitsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task DiscardSubordinateUnitChangesAsync()
    {
        if (!HasUnsavedSubordinateUnits)
        {
            return;
        }

        try
        {
            SelectedSubordinateUnit = null;
            await RefreshSubordinateUnitsAsync();
            StatusMessage = "Đã bỏ các thay đổi đơn vị trực thuộc.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Di chuyển đơn vị tới vị trí targetIndex (0-based) trong danh sách nháp.</summary>
    public void ReorderSubordinateUnit(SubordinateUnit unit, int targetIndex)
    {
        var oldIndex = SubordinateUnits.IndexOf(unit);
        if (oldIndex < 0 || targetIndex < 0 || targetIndex >= SubordinateUnits.Count || oldIndex == targetIndex)
        {
            return;
        }

        SubordinateUnits.Move(oldIndex, targetIndex);
        for (var index = 0; index < SubordinateUnits.Count; index++)
        {
            SubordinateUnits[index].SortOrder = index + 1;
        }

        SelectedSubordinateUnit = unit;
        NotifyMainDraftState();
    }

    [RelayCommand]
    private async Task DeleteSubordinateUnitAsync()
    {
        if (SelectedSubordinateUnit is null)
        {
            ShowError("Vui lòng chọn đơn vị trực thuộc cần xóa.");
            return;
        }

        var unitId = SelectedSubordinateUnit.Id;
        var unitName = SelectedSubordinateUnit.Name;
        var confirm = MessageBox.Show(
            $"Xóa đơn vị trực thuộc '{unitName}'?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await ConfirmDiscardPendingEditsAsync(
                "xóa đơn vị", dashboard: false, opening: false, units: true))
        {
            return;
        }

        try
        {
            await inventoryService.DeleteSubordinateUnitAsync(unitId);
            SetStatusAfterSave("Đã xóa đơn vị trực thuộc.");
            await RefreshSubordinateUnitsAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task RefreshSubordinateUnitsAsync()
    {
        var selectedManagedUnitId = SelectedManagedUnitIssueUnit?.UnitId;
        var units = await inventoryService.GetSubordinateUnitsAsync();
        SubordinateUnits.Clear();
        foreach (var unit in units)
        {
            SubordinateUnits.Add(unit);
        }

        UnitReportUnitOptions.Clear();
        UnitReportUnitOptions.Add(new UnitReportUnitOption(null, "Tất cả đơn vị"));
        foreach (var unit in units)
        {
            var display = string.IsNullOrWhiteSpace(unit.UnitCode)
                ? unit.Name
                : $"{unit.UnitCode} - {unit.Name}";
            UnitReportUnitOptions.Add(new UnitReportUnitOption(unit.Id, display));
        }

        SelectedManagedUnitIssueUnit = UnitReportUnitOptions.FirstOrDefault(x => x.UnitId == selectedManagedUnitId)
            ?? UnitReportUnitOptions[0];

        // Sync summary unit options (preserve selection state)
        var prevSelected = SummaryUnitOptions
            .Where(x => x.IsSelected)
            .Select(x => x.UnitId)
            .ToHashSet();
        SummaryUnitOptions.Clear();
        foreach (var unit in units)
        {
            SummaryUnitOptions.Add(new UnitSummaryUnitOption(unit.Id, unit.UnitCode, unit.Name)
            {
                IsSelected = prevSelected.Count == 0 || prevSelected.Contains(unit.Id)
            });
        }

        CaptureUnitsBaseline();
    }

    public bool HasUnsavedOpeningBalances =>
        _openingBaseline is not null
        && !string.Equals(_openingBaseline, BuildOpeningSignature(), StringComparison.Ordinal);

    public bool HasUnsavedSubordinateUnits =>
        _unitsBaseline is not null
        && !string.Equals(_unitsBaseline, BuildUnitsSignature(), StringComparison.Ordinal);

    public bool HasUnsavedDashboardMetadata =>
        DashboardRows.Any(x =>
            !_dashboardBaseline.TryGetValue(x.ItemId, out var baseline)
            || !string.Equals(baseline, BuildDashboardMetadataSignature(x), StringComparison.Ordinal));

    public bool HasUnsavedMainEdits =>
        HasUnsavedOpeningBalances
        || HasUnsavedSubordinateUnits
        || HasUnsavedDashboardMetadata
        || HasUnsavedManagedUnitIssues;

    public void CaptureOpeningBaseline()
    {
        _openingBaseline = BuildOpeningSignature();
        NotifyMainDraftState();
    }

    public void CaptureUnitsBaseline()
    {
        _unitsBaseline = BuildUnitsSignature();
        NotifyMainDraftState();
    }

    private void CaptureDashboardBaseline()
    {
        _dashboardBaseline = DashboardRows.ToDictionary(x => x.ItemId, BuildDashboardMetadataSignature);
        NotifyMainDraftState();
    }

    public void NotifyMainDraftState()
    {
        OnPropertyChanged(nameof(HasUnsavedOpeningBalances));
        OnPropertyChanged(nameof(HasUnsavedSubordinateUnits));
        OnPropertyChanged(nameof(HasUnsavedDashboardMetadata));
        OnPropertyChanged(nameof(HasUnsavedManagedUnitIssues));
        OnPropertyChanged(nameof(HasUnsavedMainEdits));
    }

    private string? _openingBaseline;
    private string? _unitsBaseline;
    private Dictionary<int, string> _dashboardBaseline = [];

    private static string BuildDashboardMetadataSignature(DashboardRowViewModel row) =>
        string.Join('\u001f',
            row.Category?.Trim() ?? string.Empty,
            NormalizeUsageTypeForSave(row.UsageType),
            row.Note?.Trim() ?? string.Empty);

    private string BuildOpeningSignature()
    {
        var visible = OpeningBalanceLines.Select(x =>
            string.Join('\u001f',
                x.Category?.Trim() ?? string.Empty,
                x.ItemName?.Trim() ?? string.Empty,
                x.Unit?.Trim() ?? string.Empty,
                x.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.UsageType?.Trim() ?? string.Empty,
                x.OpeningQuantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                x.Note?.Trim() ?? string.Empty));
        var removed = _removedOpeningBalanceLines.Keys.OrderBy(x => x).Select(x => $"removed:{x}");
        return string.Join("|", visible.Concat(removed));
    }

    private string BuildUnitsSignature() =>
        string.Join("|", SubordinateUnits.Select(x =>
            string.Join('\u001f',
                x.Id.ToString(),
                x.UnitCode?.Trim() ?? string.Empty,
                x.Name?.Trim() ?? string.Empty,
                x.SortOrder.ToString(),
                x.ReceivedBy?.Trim() ?? string.Empty,
                x.DeliveredBy?.Trim() ?? string.Empty,
                x.Reason?.Trim() ?? string.Empty,
                x.Note?.Trim() ?? string.Empty)));

    private async Task<bool> ConfirmDiscardPendingEditsAsync(
        string operation,
        bool dashboard = true,
        bool opening = true,
        bool units = true,
        bool managedUnitIssues = true)
    {
        var discardDashboard = dashboard && HasUnsavedDashboardMetadata;
        var discardOpening = opening && HasUnsavedOpeningBalances;
        var discardUnits = units && HasUnsavedSubordinateUnits;
        var discardManagedUnitIssues = managedUnitIssues && HasUnsavedManagedUnitIssues;
        if (!discardDashboard && !discardOpening && !discardUnits && !discardManagedUnitIssues)
        {
            return true;
        }

        var parts = new List<string>();
        if (discardDashboard) parts.Add("Dashboard");
        if (discardOpening) parts.Add("tồn đầu năm");
        if (discardUnits) parts.Add("đơn vị trực thuộc");
        if (discardManagedUnitIssues) parts.Add("phiếu xuất đơn vị");
        var result = MessageBox.Show(
            $"Đang có thay đổi chưa lưu: {string.Join(", ", parts)}.\n\n" +
            $"Bỏ các thay đổi để {operation}?",
            "Thay đổi chưa lưu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        if (discardDashboard) await ReloadDashboardRowsPreservingFiltersAsync();
        if (discardOpening) await ReloadOpeningBalancesFromDbAsync();
        if (discardUnits) await RefreshSubordinateUnitsAsync();
        if (discardManagedUnitIssues) await LoadManagedUnitIssuesAsync(updateStatus: false);
        return true;
    }

    private bool ConfirmOpeningBalanceChanges()
    {
        if (OpeningBalanceConfirmation is not null)
        {
            return OpeningBalanceConfirmation();
        }

        var dialog = new TypedConfirmationWindow();
        var owner = Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }

    [RelayCommand]
    public void OpenOpeningTotalAdjustWindow()
    {
        var validLines = OpeningBalanceLines
            .Select((line, index) => (line, rowNum: index + 1))
            .Where(x => x.line.OpeningQuantity > 0 && x.line.UnitPrice >= 0)
            .ToList();

        if (validLines.Count == 0)
        {
            ShowInformation("Không có dòng tồn đầu năm nào có số lượng > 0 để điều chỉnh tổng tiền.");
            return;
        }

        var inputLines = validLines
            .Select(x => new PriceAdjustmentCalculator.InputLine(
                x.rowNum,
                string.IsNullOrWhiteSpace(x.line.ItemName) ? $"Dòng {x.rowNum}" : x.line.ItemName,
                x.line.OpeningQuantity,
                x.line.UnitPrice))
            .ToList();

        var currentTotal = OpeningBalanceLines.Sum(x => x.OpeningQuantity * x.UnitPrice);
        var window = new ReceiptTotalAdjustWindow(inputLines, currentTotal, "Điều chỉnh tổng tiền tồn đầu năm")
        {
            Owner = Application.Current?.MainWindow
        };

        if (window.ShowDialog() != true || window.SelectedSuggestion is null)
        {
            return;
        }

        var byRow = validLines.ToDictionary(x => x.rowNum, x => x.line);
        foreach (var change in window.SelectedSuggestion.Changes)
        {
            if (byRow.TryGetValue(change.RowNumber, out var line))
            {
                line.UnitPrice = change.NewPrice;
            }
        }

        var newTotal = OpeningBalanceLines.Sum(x => x.OpeningQuantity * x.UnitPrice);
        NotifyMainDraftState();
        ShowInformation($"Đã áp dụng điều chỉnh đơn giá tồn đầu năm.\nTổng tiền mới: {newTotal:N0} đ.\n\nKiểm tra lại rồi bấm Lưu tồn đầu năm.");
    }

    private bool ConfirmOpeningBalanceTotalAmount()
    {
        if (OpeningBalanceConfirmation is not null)
        {
            return true;
        }

        var totalAmount = OpeningBalanceLines.Sum(x => x.OpeningQuantity * x.UnitPrice);
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            return true;
        }

        var confirm = MessageBox.Show(
            owner,
            $"Tổng tiền tồn đầu năm ({SelectedYear}): {totalAmount:N0} đ\n\nTổng tiền đã đúng chưa?",
            "Xác nhận tổng tiền tồn đầu năm",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            return true;
        }

        if (confirm == MessageBoxResult.Cancel)
        {
            return false;
        }

        var wantSuggest = MessageBox.Show(
            owner,
            "Tổng chưa đúng có thể do đơn giá đã điều chỉnh làm sai lệch tổng.\n\n"
            + "• Bấm Yes: nhập tổng tiền đúng, app sẽ gợi ý chỉnh đơn giá sao cho ít thay đổi nhất (không sửa số lượng).\n"
            + "• Bấm No: quay lại bảng tồn đầu năm để tự điều chỉnh đơn giá.",
            "Tổng tiền chưa đúng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (wantSuggest == MessageBoxResult.Yes)
        {
            OpenOpeningTotalAdjustWindow();
        }

        return false;
    }

    private void ShowInformation(string message)
    {
        StatusMessage = message;
        if (Application.Current?.MainWindow is null)
        {
            return;
        }

        MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task OpenCreateReceiptDialogAsync()
    {
        await ShowReceiptDialogAsync(receiptId: null);
    }

    [RelayCommand]
    private async Task OpenEditReceiptDialogAsync()
    {
        if (SelectedReceipt is null)
        {
            ShowError("Vui lòng chọn phiếu nhập cần sửa.");
            return;
        }

        await ShowReceiptDialogAsync(SelectedReceipt.Id);
    }

    [RelayCommand]
    private async Task DeleteReceiptAsync()
    {
        if (SelectedReceipt is null)
        {
            ShowError("Vui lòng chọn phiếu nhập cần xóa.");
            return;
        }

        var confirm = MessageBox.Show(
            $"Xóa phiếu nhập {SelectedReceipt.ReceiptNumber}?",
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await inventoryService.DeleteReceiptAsync(SelectedReceipt.Id);
            SetStatusAfterSave($"Đã xóa phiếu nhập {SelectedReceipt.ReceiptNumber}.");
            await SyncLiveDataAsync(LiveDataScope.Items | LiveDataScope.VouchersAndDashboard);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task OpenCreateIssueDialogAsync()
    {
        await ShowIssueDialogAsync(issueId: null);
    }

    [RelayCommand]
    private async Task OpenEditIssueDialogAsync()
    {
        if (SelectedIssue is null)
        {
            ShowError("Vui lòng chọn phiếu cân đối cần sửa.");
            return;
        }

        await ShowIssueDialogAsync(SelectedIssue.Id);
    }

    [RelayCommand]
    private async Task DeleteIssueAsync()
    {
        if (SelectedIssue is null)
        {
            ShowError("Vui lòng chọn phiếu cân đối cần xóa.");
            return;
        }

        if (!await ConfirmDiscardPendingEditsAsync(
                "xóa phiếu cân đối",
                dashboard: false,
                opening: false,
                units: false,
                managedUnitIssues: true))
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Xóa phiếu cân đối {SelectedIssue.IssueNumber}?" +
            (SelectedIssue.GeneratedUnitIssueCount > 0
                ? $"\n(Sẽ xóa luôn {SelectedIssue.GeneratedUnitIssueCount} phiếu xuất theo đơn vị.)"
                : string.Empty),
            "Xác nhận",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await inventoryService.DeleteIssueAsync(SelectedIssue.Id);
            SetStatusAfterSave($"Đã xóa phiếu cân đối {SelectedIssue.IssueNumber}.");
            await SyncLiveDataAsync(LiveDataScope.VouchersAndDashboard);
            await LoadManagedUnitIssuesAsync(updateStatus: false);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ExportSelectedIssueWordAsync()
    {
        try
        {
            var selected = Issues.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0 && SelectedIssue is not null)
            {
                selected.Add(SelectedIssue);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("Vui lòng chọn phiếu cân đối để xuất Word.");
            }

            if (string.IsNullOrWhiteSpace(BalanceWordTemplatePath) || !File.Exists(BalanceWordTemplatePath))
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy {WordTemplateSettingsService.BalanceTemplateFileName}.\n" +
                    "Vào Cài đặt → Template Word để chọn thư mục hoặc tạo file mẫu.");
            }

            var itemsById = Items.ToDictionary(x => x.Id);
            var overviewUnits = SubordinateUnits.ToList();

            if (selected.Count == 1)
            {
                var issueSummary = selected[0];
                var balance = await inventoryService.GetIssueByIdAsync(issueSummary.Id)
                    ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu cân đối.");
                var stockSnapshots = await inventoryService.GetBalanceStockSnapshotsAsync(balance.Id);

                var exportDialog = new BalanceExportDialogWindow(
                    balance,
                    itemsById,
                    stockSnapshots)
                {
                    Owner = Application.Current.MainWindow
                };

                if (exportDialog.ShowDialog() != true || exportDialog.Result is null)
                {
                    return;
                }

                var exportResult = exportDialog.Result;
                if (exportResult.Format == BalanceExportFormat.Word)
                {
                    var balanceOutput = SaveWordFile($"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.docx");
                    if (balanceOutput is null)
                    {
                        return;
                    }

                    await RunBusyAsync(
                        "Đang xuất Word phiếu cân đối...",
                        () => wordExportService.ExportBalanceOverview(
                            BalanceWordTemplatePath,
                            balanceOutput,
                            balance,
                            itemsById,
                            overviewUnits,
                            stockSnapshots,
                            omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                            selectedLineIds: exportResult.SelectedLineIds));
                    StatusMessage = $"Đã xuất phiếu cân đối: {balanceOutput}";
                }
                else
                {
                    var unitIssues = await inventoryService.GetGeneratedUnitIssuesAsync(balance.Id);
                    var output = SaveExcelFile($"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.xlsx");
                    if (output is null)
                    {
                        return;
                    }

                    await RunBusyAsync(
                        "Đang xuất Excel phiếu cân đối...",
                        () => _inventoryExcelExportService.ExportBalance(
                            output,
                            balance,
                            itemsById,
                            overviewUnits,
                            stockSnapshots,
                            unitIssues,
                            omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                            selectedLineIds: exportResult.SelectedLineIds));
                    StatusMessage =
                        $"Đã xuất Excel phiếu cân đối và {unitIssues.Count} phiếu đơn vị: {output}";
                }
            }
            else
            {
                var folder = PickFolder("Chọn thư mục lưu các phiếu cân đối (Word)");
                if (folder is null)
                {
                    return;
                }

                await RunBusyAsync(
                    $"Đang xuất {selected.Count} phiếu cân đối (Word)...",
                    async () =>
                    {
                        foreach (var issueSummary in selected)
                        {
                            var balance = await inventoryService.GetIssueByIdAsync(issueSummary.Id);
                            if (balance is null) continue;
                            var stockSnapshots = await inventoryService.GetBalanceStockSnapshotsAsync(balance.Id);
                            var balanceOutput = Path.Combine(folder, $"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.docx");
                            wordExportService.ExportBalanceOverview(
                                BalanceWordTemplatePath,
                                balanceOutput,
                                balance,
                                itemsById,
                                overviewUnits,
                                stockSnapshots,
                                OmitZeroStockAtIssueDate);
                        }
                    });
                StatusMessage = $"Đã xuất {selected.Count} phiếu cân đối (Word) vào thư mục: {folder}";
            }

            await SyncLiveDataAsync(LiveDataScope.VouchersAndDashboard);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ExportSelectedIssueExcelAsync()
    {
        try
        {
            var selected = Issues.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0 && SelectedIssue is not null)
            {
                selected.Add(SelectedIssue);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("Vui lòng chọn phiếu cân đối để xuất Excel.");
            }

            var itemsById = Items.ToDictionary(x => x.Id);
            var units = SubordinateUnits.ToList();

            if (selected.Count == 1)
            {
                var issueSummary = selected[0];
                var balance = await inventoryService.GetIssueByIdAsync(issueSummary.Id)
                    ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu cân đối.");
                var snapshots = await inventoryService.GetBalanceStockSnapshotsAsync(balance.Id);

                var exportDialog = new BalanceExportDialogWindow(
                    balance,
                    itemsById,
                    snapshots)
                {
                    Owner = Application.Current.MainWindow
                };

                if (exportDialog.ShowDialog() != true || exportDialog.Result is null)
                {
                    return;
                }

                var exportResult = exportDialog.Result;
                if (exportResult.Format == BalanceExportFormat.Excel)
                {
                    var unitIssues = await inventoryService.GetGeneratedUnitIssuesAsync(balance.Id);
                    var output = SaveExcelFile($"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.xlsx");
                    if (output is null)
                    {
                        return;
                    }

                    await RunBusyAsync(
                        "Đang xuất Excel phiếu cân đối...",
                        () => _inventoryExcelExportService.ExportBalance(
                            output,
                            balance,
                            itemsById,
                            units,
                            snapshots,
                            unitIssues,
                            omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                            selectedLineIds: exportResult.SelectedLineIds));
                    StatusMessage =
                        $"Đã xuất Excel phiếu cân đối và {unitIssues.Count} phiếu đơn vị: {output}";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(BalanceWordTemplatePath) || !File.Exists(BalanceWordTemplatePath))
                    {
                        throw new InvalidOperationException(
                            $"Không tìm thấy {WordTemplateSettingsService.BalanceTemplateFileName}.\n" +
                            "Vào Cài đặt → Template Word để chọn thư mục hoặc tạo file mẫu.");
                    }

                    var balanceOutput = SaveWordFile($"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.docx");
                    if (balanceOutput is null)
                    {
                        return;
                    }

                    await RunBusyAsync(
                        "Đang xuất Word phiếu cân đối...",
                        () => wordExportService.ExportBalanceOverview(
                            BalanceWordTemplatePath,
                            balanceOutput,
                            balance,
                            itemsById,
                            units,
                            snapshots,
                            omitZeroStockAtIssueDate: exportResult.OmitZeroStock,
                            selectedLineIds: exportResult.SelectedLineIds));
                    StatusMessage = $"Đã xuất phiếu cân đối: {balanceOutput}";
                }
            }
            else
            {
                var folder = PickFolder("Chọn thư mục lưu các phiếu cân đối (Excel)");
                if (folder is null)
                {
                    return;
                }

                await RunBusyAsync(
                    $"Đang xuất {selected.Count} phiếu cân đối (Excel)...",
                    async () =>
                    {
                        foreach (var issueSummary in selected)
                        {
                            var balance = await inventoryService.GetIssueByIdAsync(issueSummary.Id);
                            if (balance is null) continue;
                            var snapshots = await inventoryService.GetBalanceStockSnapshotsAsync(balance.Id);
                            var unitIssues = await inventoryService.GetGeneratedUnitIssuesAsync(balance.Id);
                            var output = Path.Combine(folder, $"SoCanDoi_{SafeFileComponent(balance.IssueNumber)}.xlsx");
                            _inventoryExcelExportService.ExportBalance(
                                output,
                                balance,
                                itemsById,
                                units,
                                snapshots,
                                unitIssues,
                                OmitZeroStockAtIssueDate);
                        }
                    });
                StatusMessage = $"Đã xuất {selected.Count} phiếu cân đối (Excel) vào thư mục: {folder}";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ExportSelectedReceiptWordAsync()
    {
        try
        {
            var selected = Receipts.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0 && SelectedReceipt is not null)
            {
                selected.Add(SelectedReceipt);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("Vui lòng chọn phiếu nhập để xuất Word.");
            }

            if (string.IsNullOrWhiteSpace(ReceiptWordTemplatePath) || !File.Exists(ReceiptWordTemplatePath))
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy {WordTemplateSettingsService.ReceiptTemplateFileName}.\n" +
                    "Vào Cài đặt → Template Word để chọn thư mục hoặc tạo file mẫu.");
            }

            var itemsById = Items.ToDictionary(x => x.Id);

            if (selected.Count == 1)
            {
                var r = selected[0];
                var doc = await inventoryService.GetReceiptByIdAsync(r.Id)
                    ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu nhập.");
                var output = SaveWordFile($"PN_{SafeFileComponent(doc.ReceiptNumber)}.docx");
                if (output is null)
                {
                    return;
                }

                await RunBusyAsync(
                    "Đang xuất Word phiếu nhập...",
                    () => wordExportService.ExportReceipt(ReceiptWordTemplatePath, output, doc, itemsById));
                StatusMessage = $"Đã xuất file Word: {output}";
            }
            else
            {
                var folder = PickFolder("Chọn thư mục lưu các phiếu nhập (Word)");
                if (folder is null)
                {
                    return;
                }

                await RunBusyAsync(
                    $"Đang xuất {selected.Count} phiếu nhập (Word)...",
                    async () =>
                    {
                        foreach (var r in selected)
                        {
                            var doc = await inventoryService.GetReceiptByIdAsync(r.Id);
                            if (doc is null) continue;
                            var output = Path.Combine(folder, $"PN_{SafeFileComponent(doc.ReceiptNumber)}.docx");
                            wordExportService.ExportReceipt(ReceiptWordTemplatePath, output, doc, itemsById);
                        }
                    });
                StatusMessage = $"Đã xuất {selected.Count} phiếu nhập (Word) vào thư mục: {folder}";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task ExportSelectedReceiptExcelAsync()
    {
        try
        {
            var selected = Receipts.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0 && SelectedReceipt is not null)
            {
                selected.Add(SelectedReceipt);
            }

            if (selected.Count == 0)
            {
                throw new InvalidOperationException("Vui lòng chọn phiếu nhập để xuất Excel.");
            }

            var itemsById = Items.ToDictionary(x => x.Id);

            if (selected.Count == 1)
            {
                var r = selected[0];
                var receipt = await inventoryService.GetReceiptByIdAsync(r.Id)
                    ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu nhập.");
                var output = SaveExcelFile($"PN_{SafeFileComponent(receipt.ReceiptNumber)}.xlsx");
                if (output is null)
                {
                    return;
                }

                await RunBusyAsync(
                    "Đang xuất Excel phiếu nhập...",
                    () => _inventoryExcelExportService.ExportReceipt(output, receipt, itemsById));
                StatusMessage = $"Đã xuất file Excel phiếu nhập: {output}";
            }
            else
            {
                var folder = PickFolder("Chọn thư mục lưu các phiếu nhập (Excel)");
                if (folder is null)
                {
                    return;
                }

                await RunBusyAsync(
                    $"Đang xuất {selected.Count} phiếu nhập (Excel)...",
                    async () =>
                    {
                        foreach (var r in selected)
                        {
                            var receipt = await inventoryService.GetReceiptByIdAsync(r.Id);
                            if (receipt is null) continue;
                            var output = Path.Combine(folder, $"PN_{SafeFileComponent(receipt.ReceiptNumber)}.xlsx");
                            _inventoryExcelExportService.ExportReceipt(output, receipt, itemsById);
                        }
                    });
                StatusMessage = $"Đã xuất {selected.Count} phiếu nhập (Excel) vào thư mục: {folder}";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task PreviewReceiptAsync(Receipt? receipt)
    {
        try
        {
            receipt ??= SelectedReceipt;
            if (receipt is null)
            {
                ShowError("Vui lòng chọn phiếu nhập để xem trước.");
                return;
            }

            var doc = await inventoryService.GetReceiptByIdAsync(receipt.Id)
                ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu nhập.");

            var window = new VoucherPreviewWindow(wordExportService, doc, Items.ToDictionary(x => x.Id), ReceiptWordTemplatePath)
            {
                Owner = Application.Current.MainWindow
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task PreviewIssueAsync(Issue? issue)
    {
        try
        {
            issue ??= SelectedIssue;
            if (issue is null)
            {
                ShowError("Vui lòng chọn phiếu cân đối để xem.");
                return;
            }

            await OpenBalanceDetailWindowAsync(issue.Id);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task ShowReceiptDialogAsync(int? receiptId)
    {
        try
        {
            var existing = receiptId.HasValue ? await inventoryService.GetReceiptByIdAsync(receiptId.Value) : null;
            // Lấy danh mục mới nhất (tránh thiếu mặt hàng vừa tạo trên phiếu).
            var catalog = await inventoryService.GetItemsAsync();
            var dialogVm = new ReceiptDialogViewModel(catalog, existing);
            var dialog = new ReceiptDialogWindow(dialogVm)
            {
                Owner = Application.Current.MainWindow
            };

            string? savedNumber = null;
            dialog.PersistAsync = async () =>
            {
                var input = dialogVm.BuildInput();
                if (receiptId.HasValue)
                {
                    await inventoryService.UpdateReceiptAsync(receiptId.Value, input);
                }
                else
                {
                    await inventoryService.CreateReceiptAsync(input);
                }

                savedNumber = input.ReceiptNumber;
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SetStatusAfterSave(receiptId.HasValue
                ? $"Đã cập nhật phiếu nhập {savedNumber}."
                : $"Đã tạo phiếu nhập {savedNumber}.");

            await SyncLiveDataAsync(LiveDataScope.Items | LiveDataScope.Receipts | LiveDataScope.Dashboard);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task ShowIssueDialogAsync(int? issueId)
    {
        try
        {
            var existing = issueId.HasValue ? await inventoryService.GetIssueByIdAsync(issueId.Value) : null;
            var dialogVm = new IssueDialogViewModel(
                Items.ToList(),
                Receipts.ToList(),
                SubordinateUnits.ToList(),
                existing,
                // Tồn theo ngày lập phiếu; khi sửa chỉ tính các phiếu đứng trước phiếu đang mở.
                async (codes, atDate) => await inventoryService.GetAvailableStockBatchAsync(codes, atDate, issueId),
                async (itemCode, atDate) => await inventoryService.GetAvailableStockAsync(itemCode, atDate, issueId),
                async receiptId => await inventoryService.GetReceiptByIdAsync(receiptId));
            await dialogVm.EnsureAvailabilityLoadedAsync();
            var dialog = new IssueDialogWindow(dialogVm)
            {
                Owner = Application.Current.MainWindow
            };

            Issue? saved = null;
            dialog.PersistAsync = async () =>
            {
                var input = dialogVm.BuildInput();
                saved = issueId.HasValue
                    ? await inventoryService.UpdateIssueAsync(issueId.Value, input)
                    : await inventoryService.CreateIssueAsync(input);
            };

            if (dialog.ShowDialog() != true || saved is null)
            {
                return;
            }

            SetStatusAfterSave(issueId.HasValue
                ? $"Đã cập nhật phiếu cân đối {saved.IssueNumber} (đã điều chỉnh tồn)."
                : $"Đã tạo phiếu cân đối {saved.IssueNumber} (đã trừ tồn).");

            await SyncLiveDataAsync(LiveDataScope.Issues | LiveDataScope.Dashboard);

            // Cho phép sửa số phiếu từng phiếu xuất con ngay sau khi lưu.
            var unitIssues = await inventoryService.GetGeneratedUnitIssuesAsync(saved.Id);
            if (unitIssues.Count > 0)
            {
                await OpenBalanceDetailWindowAsync(saved.Id);
            }
            else
            {
                if (!HasUnsavedManagedUnitIssues)
                {
                    await LoadManagedUnitIssuesAsync(updateStatus: false);
                }
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task OpenBalanceDetailWindowAsync(int balanceId)
    {
        var doc = await inventoryService.GetIssueByIdAsync(balanceId)
            ?? throw new InvalidOperationException("Không tìm thấy chi tiết phiếu cân đối.");
        var unitIssues = await inventoryService.GetGeneratedUnitIssuesAsync(balanceId);
        var stockSnapshots = await inventoryService.GetBalanceStockSnapshotsAsync(balanceId);

        var window = new BalanceIssueDetailWindow(
            wordExportService,
            inventoryService,
            doc,
            unitIssues,
            Items.ToDictionary(x => x.Id),
            IssueWordTemplatePath,
            BalanceWordTemplatePath,
            stockSnapshots,
            SubordinateUnits.ToList())
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
        await SyncLiveDataAsync(LiveDataScope.VouchersAndDashboard);
        if (!HasUnsavedManagedUnitIssues)
        {
            await LoadManagedUnitIssuesAsync(updateStatus: false);
        }
    }

    private async Task LoadItemsAsync()
    {
        var data = await inventoryService.GetItemsAsync();
        Items.Clear();
        foreach (var item in data)
        {
            Items.Add(item);
        }

        RebuildDashboardCategoryOptions();
    }

    private async Task ReloadAllDataAsync()
    {
        SelectedReceipt = null;
        SelectedIssue = null;
        SelectedSubordinateUnit = null;
        await RefreshSubordinateUnitsAsync();
        await SyncLiveDataAsync(LiveDataScope.All);
        await LoadManagedUnitIssuesAsync(updateStatus: false);
    }

    partial void OnSelectedYearChanged(int value)
    {
        ResetManagedUnitIssuePeriod(value);
        _ = SwitchWorkingYearAsync(value);
    }

    private void ResetManagedUnitIssuePeriod(int year)
    {
        ManagedUnitIssueFromDate = new DateTime(year, 1, 1);
        ManagedUnitIssueToDate = new DateTime(year, 12, 31);
    }

    [RelayCommand]
    private async Task PickWorkingYearAsync()
    {
        try
        {
            var dialog = new YearPickerWindow(SelectedYear)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() == true && dialog.SelectedYear != SelectedYear)
            {
                if (!await ConfirmDiscardPendingEditsAsync("đổi năm làm việc"))
                {
                    return;
                }

                SelectedYear = dialog.SelectedYear;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task SwitchWorkingYearAsync(int year)
    {
        try
        {
            var ok = await RunBusyAsync(
                $"Đang tải dữ liệu năm {year}...",
                async () =>
                {
                    await SyncLiveDataAsync(LiveDataScope.YearView);
                    await LoadManagedUnitIssuesAsync(updateStatus: false);
                });
            if (ok)
            {
                StatusMessage =
                    $"Đang xem dữ liệu năm {year} (đổi Năm làm việc để xem năm khác, kể cả năm trước khi chuyển kỳ).";
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task RefreshDashboardForSelectedYearAsync()
    {
        try
        {
            await RefreshDashboardCoreAsync(updateStatusMessage: true);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task SyncLiveDataAsync(LiveDataScope scope)
    {
        if (_isLiveSyncing || scope == LiveDataScope.None)
        {
            return;
        }

        _isLiveSyncing = true;
        try
        {
            if (scope.HasFlag(LiveDataScope.Items))
            {
                await LoadItemsAsync();
            }

            if (scope.HasFlag(LiveDataScope.Receipts))
            {
                await RefreshReceiptsAsync();
            }

            if (scope.HasFlag(LiveDataScope.Issues))
            {
                await RefreshIssuesAsync();
            }

            if (scope.HasFlag(LiveDataScope.Dashboard) && !HasUnsavedDashboardMetadata)
            {
                await RefreshDashboardCoreAsync(updateStatusMessage: false);
            }

            if (scope.HasFlag(LiveDataScope.OpeningBalances) && !HasUnsavedOpeningBalances)
            {
                await ReloadOpeningBalancesFromDbAsync();
            }
        }
        finally
        {
            _isLiveSyncing = false;
        }
    }

    private async Task RefreshDashboardCoreAsync(bool updateStatusMessage)
    {
        var rows = await inventoryService.GetDashboardRowsAsync(SelectedYear);
        ReplaceDashboardRows(rows);
        ResetDashboardColumnFilters();
        EnsureDashboardView().View.Refresh();
        RecalculateDashboardTotalFromView();
        if (updateStatusMessage)
        {
            StatusMessage = $"Đã tải dashboard năm {SelectedYear}.";
        }
    }

    private async Task ReloadDashboardRowsPreservingFiltersAsync()
    {
        var rows = await inventoryService.GetDashboardRowsAsync(SelectedYear);
        ReplaceDashboardRows(rows);
        EnsureDashboardView().View.Refresh();
        RecalculateDashboardTotalFromView();
    }

    private void ReplaceDashboardRows(IReadOnlyList<DashboardRow> rows)
    {
        foreach (var oldRow in DashboardRows)
        {
            oldRow.PropertyChanged -= DashboardRow_OnPropertyChanged;
        }

        // Một lần Reset thay vì Clear + N lần Add (tránh lag UI khi catalog lớn).
        var viewModels = new List<DashboardRowViewModel>(rows.Count);
        foreach (var row in rows)
        {
            var viewModel = new DashboardRowViewModel(row);
            viewModel.PropertyChanged += DashboardRow_OnPropertyChanged;
            viewModels.Add(viewModel);
        }

        DashboardRows.ReplaceAll(viewModels);
        CaptureDashboardBaseline();
    }

    private void DashboardRow_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardRowViewModel.Category)
            or nameof(DashboardRowViewModel.UsageType)
            or nameof(DashboardRowViewModel.Note))
        {
            NotifyMainDraftState();
        }
    }

    partial void OnDashboardEditUnlockedChanged(bool value)
    {
        OnPropertyChanged(nameof(DashboardGridReadOnly));
    }

    public bool DashboardGridReadOnly => !DashboardEditUnlocked;

    private CollectionViewSource EnsureDashboardView()
    {
        if (_dashboardViewSource is null)
        {
            _dashboardViewSource = new CollectionViewSource { Source = DashboardRows };
            _dashboardViewSource.Filter += DashboardView_OnFilter;
            // Mặc định khi mở app: xếp theo danh mục, rồi tên mặt hàng.
            _dashboardViewSource.SortDescriptions.Add(
                new SortDescription(nameof(DashboardRowViewModel.Category), ListSortDirection.Ascending));
            _dashboardViewSource.SortDescriptions.Add(
                new SortDescription(nameof(DashboardRowViewModel.ItemName), ListSortDirection.Ascending));
        }

        return _dashboardViewSource;
    }

    private void DashboardView_OnFilter(object sender, FilterEventArgs e)
    {
        e.Accepted = e.Item is DashboardRowViewModel row
            && PassesDashboardColumnFilters(row)
            && PassesDashboardQuickSearch(row);
    }

    private bool PassesDashboardQuickSearch(DashboardRowViewModel row)
    {
        if (_dashboardSearchNormalized.Length == 0)
        {
            return true;
        }

        return row.ItemNameSearchKey.Contains(_dashboardSearchNormalized, StringComparison.Ordinal);
    }

    private bool PassesDashboardColumnFilters(DashboardRowViewModel row)
    {
        foreach (var column in AllDashboardFilterColumns)
        {
            var set = _dashboardColumnIncluded[column];
            if (set is null)
            {
                continue;
            }

            var v = DashboardColumnCanonical(column, row);
            if (!set.Contains(v))
            {
                return false;
            }
        }

        return true;
    }

    private static string DashboardColumnCanonical(DashboardFilterColumnId column, DashboardRowViewModel row)
    {
        return column switch
        {
            DashboardFilterColumnId.ItemCode => row.ItemCode.Trim(),
            DashboardFilterColumnId.Category => string.IsNullOrWhiteSpace(row.Category) ? string.Empty : row.Category.Trim(),
            DashboardFilterColumnId.ItemName => row.ItemName.Trim(),
            DashboardFilterColumnId.Unit => row.Unit.Trim(),
            DashboardFilterColumnId.UnitPrice => CanonicalNumber(row.UnitPrice),
            DashboardFilterColumnId.UsageType => NormalizeUsageTypeForSave(row.UsageType),
            DashboardFilterColumnId.OpeningQuantity => CanonicalNumber(row.OpeningQuantity),
            DashboardFilterColumnId.ReceivedQuantity => CanonicalNumber(row.ReceivedQuantity),
            DashboardFilterColumnId.IssuedQuantity => CanonicalNumber(row.IssuedQuantity),
            DashboardFilterColumnId.ClosingQuantity => CanonicalNumber(row.ClosingQuantity),
            DashboardFilterColumnId.Amount => CanonicalNumber(row.Amount),
            DashboardFilterColumnId.Note => row.Note?.Trim() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string DashboardFilterValueDisplay(DashboardFilterColumnId column, string canon)
    {
        if (string.IsNullOrEmpty(canon))
        {
            return column == DashboardFilterColumnId.UsageType ? "Tự mua sắm" : DashboardFilterEmptyLabel;
        }

        if (column == DashboardFilterColumnId.UsageType
            && canon.Equals("trên cấp", StringComparison.OrdinalIgnoreCase))
        {
            return "Trên cấp";
        }

        if (column is DashboardFilterColumnId.UnitPrice
            or DashboardFilterColumnId.OpeningQuantity
            or DashboardFilterColumnId.ReceivedQuantity
            or DashboardFilterColumnId.IssuedQuantity
            or DashboardFilterColumnId.ClosingQuantity
            or DashboardFilterColumnId.Amount)
        {
            return decimal.TryParse(canon, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number.ToString("N0")
                : canon;
        }

        return canon;
    }

    private static string DashboardFilterColumnTitle(DashboardFilterColumnId c) =>
        c switch
        {
            DashboardFilterColumnId.ItemCode => "Mã hàng",
            DashboardFilterColumnId.Category => "Danh mục",
            DashboardFilterColumnId.ItemName => "Tên mặt hàng",
            DashboardFilterColumnId.Unit => "DVT",
            DashboardFilterColumnId.UnitPrice => "Đơn giá",
            DashboardFilterColumnId.UsageType => "Loại sử dụng",
            DashboardFilterColumnId.OpeningQuantity => "Tồn đầu năm",
            DashboardFilterColumnId.ReceivedQuantity => "Tổng nhập",
            DashboardFilterColumnId.IssuedQuantity => "Tổng xuất",
            DashboardFilterColumnId.ClosingQuantity => "Tồn cuối năm",
            DashboardFilterColumnId.Amount => "Thành tiền",
            DashboardFilterColumnId.Note => "Ghi chú",
            _ => "Cột"
        };

    private void ResetDashboardColumnFilters()
    {
        foreach (var column in AllDashboardFilterColumns)
        {
            _dashboardColumnIncluded[column] = null;
        }

        DashboardSearchText = string.Empty;
        NotifyDashboardFilterButtonLabels();
    }

    partial void OnDashboardSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDashboardSearchText));

        // Xóa tìm: áp dụng ngay; đang gõ: debounce để tránh Refresh mỗi phím.
        if (string.IsNullOrWhiteSpace(value))
        {
            _dashboardSearchDebounceTimer?.Stop();
            ApplyDashboardSearchFilter();
            return;
        }

        EnsureDashboardSearchDebounceTimer();
        _dashboardSearchDebounceTimer!.Stop();
        _dashboardSearchDebounceTimer.Start();
    }

    private void EnsureDashboardSearchDebounceTimer()
    {
        if (_dashboardSearchDebounceTimer is not null)
        {
            return;
        }

        _dashboardSearchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _dashboardSearchDebounceTimer.Tick += (_, _) =>
        {
            _dashboardSearchDebounceTimer.Stop();
            ApplyDashboardSearchFilter();
        };
    }

    private void ApplyDashboardSearchFilter()
    {
        _dashboardSearchNormalized = DashboardRowViewModel.NormalizeForSearch(DashboardSearchText?.Trim());
        EnsureDashboardView().View.Refresh();
    }

    public bool HasDashboardSearchText => !string.IsNullOrWhiteSpace(DashboardSearchText);

    [RelayCommand]
    private void ClearDashboardSearch()
    {
        DashboardSearchText = string.Empty;
    }

    private void RecalculateDashboardTotalFromView()
    {
        var view = EnsureDashboardView().View;
        decimal sum = 0;
        foreach (var o in view)
        {
            if (o is DashboardRowViewModel r)
            {
                sum += r.Amount;
            }
        }

        DashboardTotalAmount = sum;
    }

    private void NotifyDashboardFilterButtonLabels()
    {
        OnPropertyChanged(nameof(DashboardFilterButtonItemCode));
        OnPropertyChanged(nameof(DashboardFilterButtonCategory));
        OnPropertyChanged(nameof(DashboardFilterButtonItemName));
        OnPropertyChanged(nameof(DashboardFilterButtonUnit));
        OnPropertyChanged(nameof(DashboardFilterButtonUnitPrice));
        OnPropertyChanged(nameof(DashboardFilterButtonUsageType));
        OnPropertyChanged(nameof(DashboardFilterButtonOpeningQuantity));
        OnPropertyChanged(nameof(DashboardFilterButtonReceivedQuantity));
        OnPropertyChanged(nameof(DashboardFilterButtonIssuedQuantity));
        OnPropertyChanged(nameof(DashboardFilterButtonClosingQuantity));
        OnPropertyChanged(nameof(DashboardFilterButtonAmount));
        OnPropertyChanged(nameof(DashboardFilterButtonNote));
    }

    public string DashboardFilterButtonItemCode => FormatDashboardFilterButton(DashboardFilterColumnId.ItemCode);
    public string DashboardFilterButtonCategory => FormatDashboardFilterButton(DashboardFilterColumnId.Category);
    public string DashboardFilterButtonItemName => FormatDashboardFilterButton(DashboardFilterColumnId.ItemName);
    public string DashboardFilterButtonUnit => FormatDashboardFilterButton(DashboardFilterColumnId.Unit);
    public string DashboardFilterButtonUnitPrice => FormatDashboardFilterButton(DashboardFilterColumnId.UnitPrice);
    public string DashboardFilterButtonUsageType => FormatDashboardFilterButton(DashboardFilterColumnId.UsageType);
    public string DashboardFilterButtonOpeningQuantity => FormatDashboardFilterButton(DashboardFilterColumnId.OpeningQuantity);
    public string DashboardFilterButtonReceivedQuantity => FormatDashboardFilterButton(DashboardFilterColumnId.ReceivedQuantity);
    public string DashboardFilterButtonIssuedQuantity => FormatDashboardFilterButton(DashboardFilterColumnId.IssuedQuantity);
    public string DashboardFilterButtonClosingQuantity => FormatDashboardFilterButton(DashboardFilterColumnId.ClosingQuantity);
    public string DashboardFilterButtonAmount => FormatDashboardFilterButton(DashboardFilterColumnId.Amount);
    public string DashboardFilterButtonNote => FormatDashboardFilterButton(DashboardFilterColumnId.Note);

    private string FormatDashboardFilterButton(DashboardFilterColumnId column) =>
        _dashboardColumnIncluded[column] is null ? "Lọc" : "Lọc ▾";

    [RelayCommand]
    private void OpenDashboardFilter(object? parameter)
    {
        if (!Enum.TryParse(parameter?.ToString(), ignoreCase: true, out DashboardFilterColumnId column))
        {
            return;
        }

        var owner = Application.Current.MainWindow;
        var current = _dashboardColumnIncluded[column];
        var distinctCanon = DashboardRows
            .Select(r => DashboardColumnCanonical(column, r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => string.IsNullOrEmpty(c) ? "\uFFFF" : c, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var items = distinctCanon
            .Select(c => new DashboardFilterValueItem(c, DashboardFilterValueDisplay(column, c)) { IsSelected = current is null || current.Contains(c) })
            .ToList();

        var dlg = new DashboardColumnFilterWindow(DashboardFilterColumnTitle(column), items)
        {
            Owner = owner
        };

        dlg.ShowDialog();
        if (dlg.DialogResult != true)
        {
            return;
        }

        _dashboardColumnIncluded[column] = dlg.Tag as HashSet<string>;
        NotifyDashboardFilterButtonLabels();
        EnsureDashboardView().View.Refresh();
        RecalculateDashboardTotalFromView();
    }

    [RelayCommand]
    private async Task SaveDashboardChangesAsync()
    {
        try
        {
            var patches = DashboardRows
                .Where(x =>
                    !_dashboardBaseline.TryGetValue(x.ItemId, out var baseline)
                    || !string.Equals(baseline, BuildDashboardMetadataSignature(x), StringComparison.Ordinal))
                .Select(x => new ItemMetadataPatch(
                    x.ItemId,
                    x.Category.Trim(),
                    NormalizeUsageTypeForSave(x.UsageType),
                    x.Note))
                .ToList();
            if (patches.Count == 0)
            {
                StatusMessage = "Dashboard không có thay đổi cần lưu.";
                return;
            }

            var count = patches.Count;
            var ok = await RunBusyAsync(
                "Đang lưu thay đổi Dashboard...",
                async () =>
                {
                    await inventoryService.UpdateItemsMetadataAsync(patches);
                    // Unit test không có Dispatcher: tránh đụng CollectionView từ thread khác.
                    if (Application.Current?.Dispatcher is null)
                    {
                        await LoadItemsAsync();
                        RebuildDashboardCategoryOptions();
                        CaptureDashboardBaseline();
                    }
                    else
                    {
                        await ResyncAfterDashboardMetadataChangeAsync();
                    }
                });
            if (ok)
            {
                SetStatusAfterSave($"Đã lưu {count} thay đổi từ Dashboard.");
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task DiscardDashboardChangesAsync()
    {
        try
        {
            if (!HasUnsavedDashboardMetadata)
            {
                return;
            }

            await ReloadDashboardRowsPreservingFiltersAsync();
            StatusMessage = "Đã bỏ các thay đổi chưa lưu trên Dashboard.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Paste từ Excel: chỉ ghi vào cột Danh mục (1), Loại sử dụng (5), Ghi chú (11) theo DisplayIndex của lưới Dashboard.</summary>
    public async Task ApplyDashboardPasteAsync(
        IReadOnlyList<object?> viewOrderedItems,
        int startRowIndex,
        int startColumnDisplayIndex,
        IReadOnlyList<IReadOnlyList<string>> matrix)
    {
        if (!DashboardEditUnlocked || matrix.Count == 0)
        {
            return;
        }

        static bool IsEditablePasteColumn(int displayIndex) => displayIndex is 1 or 5 or 11;

        var touched = new Dictionary<int, DashboardRowViewModel>();
        for (var i = 0; i < matrix.Count; i++)
        {
            var ri = startRowIndex + i;
            if (ri < 0 || ri >= viewOrderedItems.Count)
            {
                break;
            }

            if (viewOrderedItems[ri] is not DashboardRowViewModel row)
            {
                continue;
            }

            var line = matrix[i];
            for (var j = 0; j < line.Count; j++)
            {
                var ci = startColumnDisplayIndex + j;
                if (!IsEditablePasteColumn(ci))
                {
                    continue;
                }

                if (ci == 1)
                {
                    row.Category = line[j].Trim();
                }
                else if (ci == 5)
                {
                    row.UsageType = NormalizeUsageTypeFromPaste(line[j]);
                }
                else if (ci == 11)
                {
                    row.Note = string.IsNullOrWhiteSpace(line[j]) ? null : line[j];
                }

                touched[row.ItemId] = row;
            }
        }

        if (touched.Count == 0)
        {
            return;
        }

        NotifyMainDraftState();
        StatusMessage = $"Đã dán dữ liệu vào {touched.Count} dòng (chưa lưu).";
        await Task.CompletedTask;
    }

    private static string NormalizeUsageTypeFromPaste(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
        {
            return string.Empty;
        }

        var t = cell.Trim();
        if (t.Contains("trên", StringComparison.OrdinalIgnoreCase)
            || t.Contains("tren", StringComparison.OrdinalIgnoreCase))
        {
            return "trên cấp";
        }

        return string.Empty;
    }

    private void RebuildDashboardCategoryOptions()
    {
        DashboardCategoryOptions.Clear();
        DashboardCategoryOptions.Add(new CategoryOption(DashboardFilterEmptyLabel, string.Empty));
        foreach (var c in Items
                     .Select(x => x.Category)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Select(x => x.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            DashboardCategoryOptions.Add(new CategoryOption(c, c));
        }
    }

    public async Task CommitDashboardRowEditAsync(DashboardRowViewModel row)
    {
        if (!DashboardEditUnlocked)
        {
            return;
        }

        NotifyMainDraftState();
        StatusMessage = "Dashboard có thay đổi chưa lưu.";
        await Task.CompletedTask;
    }

    private async Task ResyncAfterDashboardMetadataChangeAsync()
    {
        await LoadItemsAsync();
        RebuildDashboardCategoryOptions();
        await ReloadDashboardRowsPreservingFiltersAsync();
    }

    private async Task ReloadOpeningBalancesFromDbAsync()
    {
        var lines = await inventoryService.GetOpeningBalancesForYearAsync(SelectedYear);
        ClearOpeningBalanceLines();
        _removedOpeningBalanceLines.Clear();
        foreach (var line in lines)
        {
            AddOpeningLineToCollection(new OpeningBalanceInputViewModel(line));
        }

        CaptureOpeningBaseline();
    }

    private void AddOpeningLineToCollection(OpeningBalanceInputViewModel line)
    {
        line.PropertyChanged += OpeningLine_OnPropertyChanged;
        OpeningBalanceLines.Add(line);
    }

    private void ClearOpeningBalanceLines()
    {
        foreach (var line in OpeningBalanceLines)
        {
            line.PropertyChanged -= OpeningLine_OnPropertyChanged;
        }

        OpeningBalanceLines.Clear();
    }

    private void OpeningLine_OnPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        NotifyMainDraftState();

    private List<OpeningBalanceInput> BuildOpeningBalanceInputsForSave()
    {
        var inputs = OpeningBalanceLines.Select(ToOpeningBalanceInput).ToList();
        var visibleKeys = inputs
            .Select(GetOpeningBalanceKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        inputs.AddRange(_removedOpeningBalanceLines
            .Where(x => !visibleKeys.Contains(x.Key))
            .Select(x => x.Value));
        return inputs;
    }

    private static OpeningBalanceInput ToOpeningBalanceInput(OpeningBalanceInputViewModel line) =>
        new()
        {
            Category = line.Category.Trim(),
            ItemName = line.ItemName.Trim(),
            Unit = line.Unit.Trim(),
            UnitPrice = line.UnitPrice,
            UsageType = line.UsageType.Trim(),
            OpeningQuantity = line.OpeningQuantity,
            Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()
        };

    private async Task<bool> ResolveOpeningBalanceVariantsAsync(List<OpeningBalanceInput> lines)
    {
        var catalog = Items.ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            // Dòng xóa (OpeningQuantity = 0) không cần hỏi biến thể.
            if (lines[i].OpeningQuantity <= 0)
            {
                continue;
            }

            while (true)
            {
                var line = lines[i];
                var resolution = ItemVariantResolver.Resolve(
                    catalog,
                    itemCode: null,
                    line.ItemName,
                    line.UnitPrice,
                    line.Category,
                    line.UsageType);

                if (resolution.Kind == ItemVariantResolutionKind.ExactMatch && resolution.ExactItem is not null)
                {
                    lines[i] = ApplyCatalogToOpening(line, resolution.ExactItem);
                    break;
                }

                if (!ItemVariantResolver.NeedsUserChoice(resolution))
                {
                    break;
                }

                var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current.MainWindow;
                var dialog = new ItemVariantChoiceWindow(
                    line.ItemName,
                    line.ItemName,
                    line.UnitPrice,
                    line.Category,
                    line.UsageType,
                    line.Unit,
                    resolution.Candidates)
                {
                    Owner = owner
                };

                if (dialog.ShowDialog() != true || dialog.Choice == ItemVariantChoiceAction.Cancel)
                {
                    return false;
                }

                if (dialog.Choice == ItemVariantChoiceAction.UseExisting && dialog.SelectedItem is not null)
                {
                    lines[i] = ApplyCatalogToOpening(line, dialog.SelectedItem);
                    break;
                }

                line.Category = dialog.SelectedCategory;
                line.UsageType = dialog.SelectedUsageType;
                lines[i] = line;

                var wouldExact = resolution.Candidates.Any(c =>
                    ItemVariantKey.Matches(c, line.ItemName, line.UnitPrice, line.Category, line.UsageType));
                if (wouldExact)
                {
                    MessageBox.Show(
                        owner,
                        "Tổ hợp danh mục + loại đã chọn trùng biến thể sẵn có.\nHãy chọn lại hoặc dùng mã đã có.",
                        "Không thể tạo trùng biến thể",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                break;
            }
        }

        await Task.CompletedTask;
        return true;
    }

    private bool CheckDuplicateOpeningBalanceUnitPrices(List<OpeningBalanceInput> lines)
    {
        var activeInputs = lines.Where(x => x.OpeningQuantity > 0).ToList();
        var catalog = Items.ToList();
        var docLines = activeInputs
            .Select((x, idx) =>
            {
                var matchedItem = catalog.FirstOrDefault(item =>
                    string.Equals(item.ItemName, x.ItemName, StringComparison.OrdinalIgnoreCase) &&
                    item.UnitPrice == x.UnitPrice &&
                    string.Equals(item.Category ?? string.Empty, x.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.UsageType ?? string.Empty, x.UsageType ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                return new DuplicateUnitPriceChecker.ItemLineInput(
                    idx + 1,
                    x.ItemName,
                    matchedItem?.ItemCode ?? string.Empty,
                    x.UnitPrice);
            })
            .ToList();

        var groups = DuplicateUnitPriceChecker.FindDuplicateGroups(docLines, catalog);
        if (groups.Count == 0)
        {
            return true;
        }

        if (Application.Current?.MainWindow is null)
        {
            return true;
        }

        var owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current.MainWindow;
        var dialog = new DuplicateUnitPriceCheckWindow(groups) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    private static OpeningBalanceInput ApplyCatalogToOpening(OpeningBalanceInput line, Item item) =>
        new()
        {
            Category = item.Category,
            ItemName = item.ItemName,
            Unit = string.IsNullOrWhiteSpace(line.Unit) ? item.Unit : line.Unit.Trim(),
            UnitPrice = item.UnitPrice,
            UsageType = item.UsageType,
            OpeningQuantity = line.OpeningQuantity,
            Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()
        };

    private static string GetOpeningBalanceKey(OpeningBalanceInput line) =>
        ItemVariantKey.Build(line.ItemName, line.UnitPrice, line.Category, line.UsageType);

    private static string NormalizeUsageTypeForSave(string usageType) =>
        ItemVariantKey.NormalizeUsageType(usageType);

    private static string CanonicalNumber(decimal value)
    {
        return value.ToString("0.################", CultureInfo.InvariantCulture);
    }

    private static string? PickExcelFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? SaveWordFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = FileNameSanitizer.Sanitize(defaultName),
            Filter = "Word Files (*.docx)|*.docx"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? SaveBackupFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = FileNameSanitizer.Sanitize(defaultName),
            Filter = "SQLite Database (*.db)|*.db"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? SaveExcelFile(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = FileNameSanitizer.Sanitize(defaultName),
            Filter = "Excel Files (*.xlsx)|*.xlsx"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string SafeFileComponent(string value) => FileNameSanitizer.Sanitize(value);

    private async Task<bool> RunBusyAsync(string message, Action work) =>
        await RunBusyAsync(message, () => Task.Run(work));

    /// <summary>
    /// Hiển thị overlay tiến trình. Dùng cho tác vụ async (DB/UI) — không đẩy sang thread khác.
    /// </summary>
    private async Task<bool> RunBusyAsync(string message, Func<Task> work)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        BusyMessage = message;
        StatusMessage = message;
        try
        {
            // Nhường UI một lượt để lớp phủ tiến trình kịp hiển thị trước khi chạy tác vụ nặng.
            await Task.Yield();
            await work();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearItemForm()
    {
        ItemCode = string.Empty;
        ItemName = string.Empty;
        ItemCategory = string.Empty;
        ItemUnit = string.Empty;
        ItemUsageType = string.Empty;
        ItemUnitPrice = "0";
    }

    private void ShowError(string message)
    {
        StatusMessage = $"Lỗi: {message}";
        // Unit test / không có UI: không gọi MessageBox (tránh treo chờ bấm OK).
        if (Application.Current?.MainWindow is null)
        {
            return;
        }

        MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowError(Exception ex) => ShowError(ExceptionMessageFormatter.Format(ex));

    private void ShowFileWriteError(Exception ex, string filePath) =>
        ShowError(ExceptionMessageFormatter.Format(ex, filePath));

    private void SetStatusAfterSave(string successMessage) => StatusMessage = successMessage;
}

public sealed partial class ManagedUnitIssueRow : ObservableObject
{
    private readonly string _baselineIssueNumber;
    private readonly DateTime _baselineIssueDate;
    private readonly string _baselineReceivedBy;
    private readonly string _baselineReason;

    public ManagedUnitIssueRow(UnitIssueReportRow source)
    {
        IssueId = source.IssueId;
        ParentIssueId = source.ParentIssueId;
        ParentIssueNumber = source.ParentIssueNumber;
        SubordinateUnitId = source.SubordinateUnitId;
        UnitCode = source.UnitCode;
        UnitName = source.UnitName;
        DeliveredBy = source.DeliveredBy;
        LineCount = source.LineCount;
        TotalQuantity = source.TotalQuantity;
        TotalAmount = source.TotalAmount;
        _baselineIssueNumber = source.IssueNumber;
        _baselineIssueDate = source.IssueDate.Date;
        _baselineReceivedBy = source.ReceivedBy;
        _baselineReason = source.Reason;
        issueNumber = source.IssueNumber;
        issueDate = source.IssueDate.Date;
        receivedBy = source.ReceivedBy;
        reason = source.Reason;
    }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string issueNumber;
    [ObservableProperty] private DateTime issueDate;
    [ObservableProperty] private string receivedBy;
    [ObservableProperty] private string reason;

    public int IssueId { get; }
    public int ParentIssueId { get; }
    public string ParentIssueNumber { get; }
    public int? SubordinateUnitId { get; }
    public string UnitCode { get; }
    public string UnitName { get; }
    public string DeliveredBy { get; }
    public int LineCount { get; }
    public decimal TotalQuantity { get; }
    public decimal TotalAmount { get; }
    public bool IsDirty =>
        !string.Equals(_baselineIssueNumber, IssueNumber?.Trim(), StringComparison.Ordinal)
        || _baselineIssueDate != IssueDate.Date
        || !string.Equals(_baselineReceivedBy, ReceivedBy?.Trim(), StringComparison.Ordinal)
        || !string.Equals(_baselineReason, Reason?.Trim(), StringComparison.Ordinal);

    public UnitIssueHeaderPatch ToHeaderPatch() => new()
    {
        IssueId = IssueId,
        IssueNumber = IssueNumber,
        IssueDate = IssueDate,
        ReceivedBy = ReceivedBy,
        DeliveredBy = DeliveredBy,
        Reason = Reason
    };

    partial void OnIssueNumberChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnIssueDateChanged(DateTime value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnReceivedByChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnReasonChanged(string value) => OnPropertyChanged(nameof(IsDirty));
}

public sealed partial class UnitAllocationInputViewModel : ObservableObject
{
    public int UnitId { get; }
    public string UnitName { get; }

    [ObservableProperty] private decimal? quantity;

    public event EventHandler? ValueChanged;

    public UnitAllocationInputViewModel(int unitId, string unitName)
    {
        UnitId = unitId;
        UnitName = unitName;
    }

    partial void OnQuantityChanged(decimal? value) => ValueChanged?.Invoke(this, EventArgs.Empty);
}

public sealed partial class LineInputViewModel : ObservableObject
{
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private int rowNumber;
    [ObservableProperty] private string itemCode = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string category = string.Empty;
    [ObservableProperty] private string unit = string.Empty;
    [ObservableProperty] private string usageType = string.Empty;
    [ObservableProperty] private decimal? quantity;
    [ObservableProperty] private decimal? unitPrice;
    [ObservableProperty] private string? note;
    [ObservableProperty] private decimal stockAtIssueDateQuantity;
    [ObservableProperty] private decimal availableQuantity;
    /// <summary>Dòng đã gắn mã danh mục — khóa danh mục/ĐVT/loại.</summary>
    [ObservableProperty] private bool isCatalogBound;
    /// <summary>Người dùng đã chọn tạo biến thể mới dù trùng tên+giá.</summary>
    [ObservableProperty] private bool forceCreateNewVariant;

    public bool HasInsufficientStock => AvailableQuantity < 0;
    public decimal StockShortageQuantity => HasInsufficientStock ? -AvailableQuantity : 0;
    /// <summary>Thành tiền dòng = Số lượng × Đơn giá (chỉ hiển thị).</summary>
    public decimal Amount => (Quantity ?? 0m) * (UnitPrice ?? 0m);

    public ObservableCollection<UnitAllocationInputViewModel> UnitAllocations { get; } = [];

    public event EventHandler? ItemCodeChanged;
    public event EventHandler? ValueChanged;

    public LineInputViewModel()
    {
    }

    public LineInputViewModel(TransactionLineInput source)
    {
        ItemCode = source.ItemCode;
        ItemName = source.ItemName;
        Category = source.Category;
        Unit = source.Unit;
        UsageType = source.UsageType;
        Quantity = source.Quantity;
        UnitPrice = source.UnitPrice;
        Note = source.Note;
    }

    public void BindToCatalogItem(Item item, bool overwriteUnitPrice = false)
    {
        ItemCode = item.ItemCode;
        ItemName = item.ItemName;
        Category = item.Category;
        Unit = item.Unit;
        UsageType = item.UsageType;
        if (overwriteUnitPrice || UnitPrice is null)
        {
            UnitPrice = item.UnitPrice;
        }

        IsCatalogBound = true;
        ForceCreateNewVariant = false;
    }

    public void MarkCreateNewVariant()
    {
        IsCatalogBound = false;
        ForceCreateNewVariant = true;
        // Xóa mã cũ (có thể trùng mã biến thể khác) để service tạo mã mới.
        ItemCode = string.Empty;
    }

    public void InitializeUnitAllocations(
        IReadOnlyCollection<SubordinateUnit> units,
        IReadOnlyDictionary<int, decimal>? existing = null)
    {
        foreach (var allocation in UnitAllocations)
        {
            allocation.ValueChanged -= OnUnitAllocationChanged;
        }

        UnitAllocations.Clear();
        foreach (var unit in units.OrderBy(x => x.SortOrder).ThenBy(x => x.Name))
        {
            decimal? qty = null;
            if (existing?.TryGetValue(unit.Id, out var existingQty) == true && existingQty > 0)
            {
                qty = existingQty;
            }

            var allocation = new UnitAllocationInputViewModel(unit.Id, unit.Name)
            {
                Quantity = qty
            };
            allocation.ValueChanged += OnUnitAllocationChanged;
            UnitAllocations.Add(allocation);
        }

        SyncQuantityFromAllocations();
    }

    public Dictionary<int, decimal> GetAllocationsDictionary() =>
        UnitAllocations
            .Where(x => x.Quantity is > 0)
            .ToDictionary(x => x.UnitId, x => x.Quantity!.Value);

    private void OnUnitAllocationChanged(object? sender, EventArgs e) => SyncQuantityFromAllocations();

    private void SyncQuantityFromAllocations()
    {
        if (UnitAllocations.Count == 0)
        {
            return;
        }

        var sum = UnitAllocations.Sum(x => x.Quantity ?? 0m);
        Quantity = sum;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnItemNameChanged(string value)
    {
        // Khi sửa tên mặt hàng, nếu tên mới khác tên catalog đang gắn -> hủy gắn mã cũ để resolver so sánh theo tên mới
        if (IsCatalogBound)
        {
            IsCatalogBound = false;
            ItemCode = string.Empty;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCategoryChanged(string value)
    {
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnUnitChanged(string value) => ValueChanged?.Invoke(this, EventArgs.Empty);

    partial void OnUsageTypeChanged(string value)
    {
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNoteChanged(string? value) => ValueChanged?.Invoke(this, EventArgs.Empty);

    partial void OnItemCodeChanged(string value)
    {
        // ComboBox/binding có thể gán null dù property khai báo string.
        if (value is null)
        {
            itemCode = string.Empty;
        }

        ItemCodeChanged?.Invoke(this, EventArgs.Empty);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnQuantityChanged(decimal? value)
    {
        OnPropertyChanged(nameof(Amount));
        if (UnitAllocations.Count > 0)
        {
            return;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnUnitPriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(Amount));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnAvailableQuantityChanged(decimal value)
    {
        OnPropertyChanged(nameof(HasInsufficientStock));
        OnPropertyChanged(nameof(StockShortageQuantity));
    }
}

public sealed partial class OpeningBalanceInputViewModel : ObservableObject
{
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string category = string.Empty;
    [ObservableProperty] private string itemName = string.Empty;
    [ObservableProperty] private string unit = string.Empty;
    [ObservableProperty] private decimal unitPrice;
    [ObservableProperty] private string usageType = string.Empty;
    [ObservableProperty] private decimal openingQuantity;
    [ObservableProperty] private string? note;

    public OpeningBalanceInputViewModel()
    {
    }

    public OpeningBalanceInputViewModel(OpeningBalanceInput source)
    {
        Category = source.Category;
        ItemName = source.ItemName;
        Unit = source.Unit;
        UnitPrice = source.UnitPrice;
        UsageType = source.UsageType;
        OpeningQuantity = source.OpeningQuantity;
        Note = source.Note;
    }
}
