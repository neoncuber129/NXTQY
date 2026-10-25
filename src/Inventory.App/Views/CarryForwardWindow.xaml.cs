using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Inventory.Domain.Models;
using Microsoft.Win32;

namespace Inventory.App.Views;

public partial class CarryForwardWindow : Window
{
    public CarryForwardViewModel ViewModel { get; }

    public CarryForwardWindow(int workingYear, YearStockActivitySummary fromYear, YearStockActivitySummary toYear)
    {
        ViewModel = new CarryForwardViewModel(workingYear, fromYear, toYear);
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void BrowseBackup_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(ViewModel.BackupFilePath),
            Filter = "SQLite Database (*.db)|*.db",
            InitialDirectory = Path.GetDirectoryName(ViewModel.BackupFilePath) is { Length: > 0 } dir && Directory.Exists(dir)
                ? dir
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.BackupFilePath = dialog.FileName;
        }
    }

    private void Confirm_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanExecute)
        {
            return;
        }

        if (ViewModel.CreateBackup && string.IsNullOrWhiteSpace(ViewModel.BackupFilePath))
        {
            MessageBox.Show(this, "Vui lòng chọn đường dẫn file backup.", "Thiếu backup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}

public sealed class CarryForwardViewModel : INotifyPropertyChanged
{
    private readonly YearStockActivitySummary _fromYearActivity;
    private readonly YearStockActivitySummary _toYearActivity;
    private bool _excludeZeroClosingStock = UiPreferenceService.LoadExcludeZeroClosingStockOnCarryForward();
    private bool _allowUpdateOpeningWithExistingActivity;
    private bool _createBackup = true;
    private string _backupFilePath;
    private string _statusMessage = string.Empty;
    private Brush _statusBrush = Brushes.Black;
    private bool _canExecute;

    public CarryForwardViewModel(int workingYear, YearStockActivitySummary fromYear, YearStockActivitySummary toYear)
    {
        _fromYearActivity = fromYear;
        _toYearActivity = toYear;
        WorkingYear = workingYear;
        FromYear = fromYear.Year;
        ToYear = toYear.Year;
        OpenedAt = DateTime.Now;

        OperationName = $"Chuyển kỳ {FromYear} → {ToYear}";
        OperationSubtitle = "Sao tồn cuối năm nguồn thành tồn đầu năm đích trong cùng một database.";
        TransferDescription =
            $"Tồn cuối năm {FromYear} của từng mặt hàng sẽ thành tồn đầu năm {ToYear}. " +
            "Khi năm đích đã có phát sinh, hệ thống sẽ cập nhật tồn đầu và tính lại sổ chi tiết (không ghi đè phiếu nhập/xuất).";

        FromYearHeader = $"Năm nguồn {FromYear}";
        ToYearHeader = $"Năm đích {ToYear}";
        FromSummaryText =
            $"• Số mặt hàng có kỳ tồn: {fromYear.PeriodCount:N0}\n" +
            $"  - Tồn cuối > 0: {fromYear.PositiveClosingCount:N0}\n" +
            $"  - Tồn cuối = 0: {fromYear.ZeroClosingCount:N0}\n" +
            $"• Mặt hàng đã nhập/xuất: {fromYear.ItemsWithMovementCount:N0}\n" +
            $"• Tổng nhập (kỳ): {fromYear.TotalReceived:N0}\n" +
            $"• Tổng xuất (kỳ): {fromYear.TotalIssued:N0}\n" +
            $"• Phiếu nhập: {fromYear.ReceiptVoucherCount:N0}\n" +
            $"• Phiếu cân đối: {fromYear.BalanceIssueVoucherCount:N0}";

        ToSummaryText =
            $"• Số dòng tồn đầu hiện có: {toYear.PeriodCount:N0}\n" +
            $"• Mặt hàng đã nhập/xuất: {toYear.ItemsWithMovementCount:N0}\n" +
            $"• Phiếu nhập: {toYear.ReceiptVoucherCount:N0}\n" +
            $"• Phiếu cân đối: {toYear.BalanceIssueVoucherCount:N0}";

        AllowUpdateOpeningText = $"Cho phép cập nhật lại tồn đầu năm {ToYear} từ tồn cuối năm {FromYear} (giữ nguyên các phiếu đã lập)";

        UpdateExecutionStatus();

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _backupFilePath = Path.Combine(docs, $"NXT_{FromYear}_cuoi_nam_{OpenedAt:yyyyMMdd_HHmmss}.db");
        BackupHint = $"File backup mặc định gắn nhãn năm nguồn ({FromYear}). Đây là bản sao toàn bộ database.";
    }

    private void UpdateExecutionStatus()
    {
        if (_fromYearActivity.PeriodCount == 0)
        {
            CanExecute = false;
            StatusMessage = $"Không thể chuyển kỳ: chưa có dữ liệu tồn năm {FromYear}.";
            StatusBrush = Brushes.DarkRed;
        }
        else if (_toYearActivity.HasStockOrVoucherActivity)
        {
            if (_allowUpdateOpeningWithExistingActivity)
            {
                CanExecute = true;
                StatusMessage =
                    $"Sẵn sàng cập nhật lại tồn đầu năm {ToYear} theo tồn cuối năm {FromYear} và đồng bộ sổ chi tiết. " +
                    "Các phiếu nhập/xuất đã lập trong năm sẽ được bảo toàn nguyên vẹn.";
                StatusBrush = Brushes.DarkOrange;
            }
            else
            {
                CanExecute = false;
                StatusMessage =
                    $"Năm {ToYear} đã có {(_toYearActivity.ReceiptVoucherCount + _toYearActivity.BalanceIssueVoucherCount)} phiếu phát sinh. " +
                    "Tick chọn 'Cho phép cập nhật lại tồn đầu' bên dưới nếu bạn muốn đồng bộ lại số tồn đầu từ năm trước.";
                StatusBrush = Brushes.DarkRed;
            }
        }
        else
        {
            CanExecute = true;
            StatusMessage = _toYearActivity.PeriodCount > 0
                ? $"Năm {ToYear} đã có tồn đầu nhưng chưa phát sinh — sẽ ghi đè tồn đầu từ năm {FromYear}."
                : $"Sẵn sàng tạo tồn đầu năm {ToYear} từ tồn cuối năm {FromYear}.";
            StatusBrush = Brushes.DarkGreen;
        }
    }

    public int WorkingYear { get; }
    public int FromYear { get; }
    public int ToYear { get; }
    public DateTime OpenedAt { get; }
    public string OperationName { get; }
    public string OperationSubtitle { get; }
    public string TransferDescription { get; }
    public string FromYearHeader { get; }
    public string ToYearHeader { get; }
    public string FromSummaryText { get; }
    public string ToSummaryText { get; }
    public string AllowUpdateOpeningText { get; }
    public bool HasTargetActivity => _toYearActivity.HasStockOrVoucherActivity;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set
        {
            if (Equals(_statusBrush, value)) return;
            _statusBrush = value;
            OnPropertyChanged();
        }
    }

    public bool CanExecute
    {
        get => _canExecute;
        private set
        {
            if (_canExecute == value) return;
            _canExecute = value;
            OnPropertyChanged();
        }
    }

    public string BackupHint { get; }

    public string OpenedAtText => OpenedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string WorkingYearText => WorkingYear.ToString();

    public bool AllowUpdateOpeningWithExistingActivity
    {
        get => _allowUpdateOpeningWithExistingActivity;
        set
        {
            if (_allowUpdateOpeningWithExistingActivity == value) return;
            _allowUpdateOpeningWithExistingActivity = value;
            OnPropertyChanged();
            UpdateExecutionStatus();
        }
    }

    public bool ExcludeZeroClosingStock
    {
        get => _excludeZeroClosingStock;
        set
        {
            if (_excludeZeroClosingStock == value)
            {
                return;
            }

            _excludeZeroClosingStock = value;
            UiPreferenceService.SaveExcludeZeroClosingStockOnCarryForward(value);
            OnPropertyChanged();
        }
    }

    public bool CreateBackup
    {
        get => _createBackup;
        set
        {
            if (_createBackup == value)
            {
                return;
            }

            _createBackup = value;
            OnPropertyChanged();
        }
    }

    public string BackupFilePath
    {
        get => _backupFilePath;
        set
        {
            if (_backupFilePath == value)
            {
                return;
            }

            _backupFilePath = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
