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
    private bool _createBackup = true;
    private string _backupFilePath;

    public CarryForwardViewModel(int workingYear, YearStockActivitySummary fromYear, YearStockActivitySummary toYear)
    {
        WorkingYear = workingYear;
        FromYear = fromYear.Year;
        ToYear = toYear.Year;
        OpenedAt = DateTime.Now;

        OperationName = $"Chuyển kỳ {FromYear} → {ToYear}";
        OperationSubtitle = "Sao tồn cuối năm nguồn thành tồn đầu năm đích trong cùng một database.";
        TransferDescription =
            $"Tồn cuối năm {FromYear} của từng mặt hàng sẽ thành tồn đầu năm {ToYear}. " +
            "Tổng nhập/xuất năm đích được đặt về 0 (chỉ khi năm đích chưa có phát sinh).";

        FromYearHeader = $"Năm nguồn {FromYear}";
        ToYearHeader = $"Năm đích {ToYear}";
        FromSummaryText =
            $"• Số mặt hàng có kỳ tồn: {fromYear.PeriodCount:N0}\n" +
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

        if (fromYear.PeriodCount == 0)
        {
            CanExecute = false;
            StatusMessage = $"Không thể chuyển kỳ: chưa có dữ liệu tồn năm {FromYear}.";
            StatusBrush = Brushes.DarkRed;
        }
        else if (toYear.HasStockOrVoucherActivity)
        {
            CanExecute = false;
            StatusMessage =
                $"Không thể chuyển kỳ: năm {ToYear} đã có nhập/xuất hoặc phiếu phát sinh. " +
                "Tránh ghi đè dữ liệu đang dùng.";
            StatusBrush = Brushes.DarkRed;
        }
        else
        {
            CanExecute = true;
            StatusMessage = toYear.PeriodCount > 0
                ? $"Năm {ToYear} đã có tồn đầu nhưng chưa phát sinh — sẽ ghi đè tồn đầu từ năm {FromYear}."
                : $"Sẵn sàng tạo tồn đầu năm {ToYear} từ tồn cuối năm {FromYear}.";
            StatusBrush = Brushes.DarkGreen;
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _backupFilePath = Path.Combine(docs, $"NXT_{FromYear}_cuoi_nam_{OpenedAt:yyyyMMdd_HHmmss}.db");
        BackupHint = $"File backup mặc định gắn nhãn năm nguồn ({FromYear}). Đây là bản sao toàn bộ database.";
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
    public string StatusMessage { get; }
    public Brush StatusBrush { get; }
    public bool CanExecute { get; }
    public string BackupHint { get; }

    public string OpenedAtText => OpenedAt.ToString("dd/MM/yyyy HH:mm:ss");
    public string WorkingYearText => WorkingYear.ToString();

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
