using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Inventory.App.Helpers;
using Inventory.Domain.Models;
using Inventory.Domain.Services;
using Inventory.Reporting.Services;
using Microsoft.Win32;

namespace Inventory.App.Views;

public partial class ReceiptDialogWindow : Window
{
    private readonly ReceiptDialogViewModel _viewModel;
    private readonly ExcelImportService _excelImportService = new();
    private bool _suppressClosePrompt;

    /// <summary>Lưu xuống DB trước khi đóng cửa sổ. Lỗi → giữ popup, chỉ báo thông báo.</summary>
    public Func<Task>? PersistAsync { get; set; }

    public ReceiptDialogWindow(ReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_suppressClosePrompt || !_viewModel.IsDirty)
        {
            return;
        }

        var discard = MessageBox.Show(
            this,
            "Phiếu nhập có thay đổi chưa lưu.\n\nBỏ các thay đổi và đóng cửa sổ?",
            "Thay đổi chưa lưu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        e.Cancel = discard != MessageBoxResult.Yes;
        _suppressClosePrompt = !e.Cancel;
    }

    private void Save_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Commit ô đang sửa (ghi chú/số lượng/…) trước khi Lưu — tránh mất thay đổi hoặc nút tưởng như không lưu.
        CommitPendingLineEdits();
    }

    private void CommitPendingLineEdits()
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        LinesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox textBox)
        {
            textBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        CommitPendingLineEdits();

        if (!_viewModel.Validate(out var message))
        {
            MessageBox.Show(this, message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ResolveItemVariants())
        {
            return;
        }

        if (!CheckDuplicateUnitPrices())
        {
            return;
        }

        if (!ConfirmTotalAmount())
        {
            return;
        }

        if (PersistAsync is not null)
        {
            try
            {
                BusyOverlay.Message = "Đang lưu phiếu nhập...";
                BusyOverlay.Visibility = Visibility.Visible;
                await Task.Yield();
                await PersistAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ExceptionMessageFormatter.Format(ex),
                    "Không lưu được phiếu",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            finally
            {
                BusyOverlay.Visibility = Visibility.Collapsed;
            }
        }

        _viewModel.AcceptChanges();
        _suppressClosePrompt = true;
        DialogResult = true;
    }

    /// <summary>
    /// Hỏi chọn biến thể khi trùng tên+đơn giá với mặt hàng đã có nhưng chưa khớp danh mục/loại.
    /// </summary>
    private bool ResolveItemVariants()
    {
        foreach (var line in _viewModel.Lines.ToList())
        {
            while (true)
            {
                var resolution = ItemVariantResolver.Resolve(
                    _viewModel.Items.ToList(),
                    line.ItemCode,
                    line.ItemName,
                    line.UnitPrice,
                    line.Category,
                    line.UsageType,
                    line.ForceCreateNewVariant);

                if (resolution.Kind == ItemVariantResolutionKind.ExactMatch && resolution.ExactItem is not null)
                {
                    line.BindToCatalogItem(resolution.ExactItem);
                    break;
                }

                if (!ItemVariantResolver.NeedsUserChoice(resolution))
                {
                    break;
                }

                var label = $"#{line.RowNumber} {(string.IsNullOrWhiteSpace(line.ItemName) ? line.ItemCode : line.ItemName)}";
                var dialog = new ItemVariantChoiceWindow(
                    label,
                    line.ItemName?.Trim() ?? string.Empty,
                    line.UnitPrice ?? 0m,
                    line.Category,
                    line.UsageType,
                    line.Unit,
                    resolution.Candidates)
                {
                    Owner = this
                };

                if (dialog.ShowDialog() != true || dialog.Choice == ItemVariantChoiceAction.Cancel)
                {
                    return false;
                }

                if (dialog.Choice == ItemVariantChoiceAction.UseExisting && dialog.SelectedItem is not null)
                {
                    line.BindToCatalogItem(dialog.SelectedItem, overwriteUnitPrice: false);
                    break;
                }

                // Áp danh mục / loại người dùng đã bấm chọn, rồi tạo mã mới.
                line.Category = dialog.SelectedCategory;
                line.UsageType = dialog.SelectedUsageType;
                var wouldExact = resolution.Candidates.Any(c =>
                    ItemVariantKey.Matches(c, line.ItemName, line.UnitPrice ?? 0m, line.Category, line.UsageType));
                if (wouldExact)
                {
                    MessageBox.Show(
                        this,
                        "Tổ hợp danh mục + loại đã chọn trùng biến thể sẵn có.\nHãy chọn lại hoặc xác nhận dùng mã đã có.",
                        "Không thể tạo trùng biến thể",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    continue;
                }

                line.MarkCreateNewVariant();
                break;
            }
        }

        return true;
    }

    private bool CheckDuplicateUnitPrices()
    {
        var docLines = _viewModel.Lines
            .Where(x => x.UnitPrice.HasValue && x.UnitPrice.Value >= 0)
            .Select(x => new DuplicateUnitPriceChecker.ItemLineInput(
                x.RowNumber,
                x.ItemName,
                x.ItemCode,
                x.UnitPrice!.Value))
            .ToList();

        // Khi sửa phiếu nhập, nếu có mặt hàng cũ bị gỡ hoàn toàn khỏi phiếu và không còn ở đâu khác,
        // nó sẽ bị xóa khỏi catalog. Không đưa các mã hàng bị gỡ đó vào so sánh trùng đơn giá trên Dashboard.
        var activeCatalog = _viewModel.Items.ToList();
        if (_viewModel.IsEditMode && _viewModel.InitialItemCodes.Count > 0)
        {
            var currentLineCodes = _viewModel.Lines
                .Select(x => x.ItemCode?.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var removedCodes = _viewModel.InitialItemCodes.Except(currentLineCodes).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (removedCodes.Count > 0)
            {
                activeCatalog = activeCatalog.Where(item => !removedCodes.Contains(item.ItemCode)).ToList();
            }
        }

        var groups = DuplicateUnitPriceChecker.FindDuplicateGroups(docLines, activeCatalog);
        if (groups.Count == 0)
        {
            return true;
        }

        var dialog = new DuplicateUnitPriceCheckWindow(groups) { Owner = this };
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Hỏi xác nhận tổng tiền trước khi lưu. Nếu chưa đúng, gợi ý nguyên nhân VAT
    /// và cho phép nhập tổng tiền đúng để app gợi ý chỉnh đơn giá (không đổi số lượng).
    /// </summary>
    private bool ConfirmTotalAmount()
    {
        var confirm = MessageBox.Show(
            this,
            $"Tổng tiền phiếu nhập: {_viewModel.TotalAmount:N0} đ\n({_viewModel.TotalAmountText})\n\nTổng tiền đã đúng chưa?",
            "Xác nhận tổng tiền",
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

        // Tổng chưa đúng — thường do đơn giá điều chỉnh theo VAT làm lệch tổng.
        var wantSuggest = MessageBox.Show(
            this,
            "Tổng chưa đúng có thể do đơn giá đã điều chỉnh theo VAT làm sai lệch tổng.\n\n"
            + "• Bấm Yes: nhập tổng tiền đúng, app sẽ gợi ý chỉnh đơn giá sao cho ít thay đổi nhất (không sửa số lượng).\n"
            + "• Bấm No: quay lại phiếu để tự điều chỉnh đơn giá.",
            "Tổng tiền chưa đúng",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (wantSuggest == MessageBoxResult.Yes)
        {
            OpenTotalAdjustWindow();
        }

        return false;
    }

    private void OpenTotalAdjustWindow()
    {
        var inputLines = _viewModel.Lines
            .Where(x => x.Quantity is > 0 && x.UnitPrice is >= 0)
            .Select(x => new PriceAdjustmentCalculator.InputLine(
                x.RowNumber,
                string.IsNullOrWhiteSpace(x.ItemName) ? (x.ItemCode ?? string.Empty) : x.ItemName,
                x.Quantity!.Value,
                x.UnitPrice!.Value))
            .ToList();

        var window = new ReceiptTotalAdjustWindow(inputLines, _viewModel.TotalAmount) { Owner = this };
        if (window.ShowDialog() != true || window.SelectedSuggestion is null)
        {
            return;
        }

        var byRow = _viewModel.Lines.ToDictionary(x => x.RowNumber);
        foreach (var change in window.SelectedSuggestion.Changes)
        {
            if (byRow.TryGetValue(change.RowNumber, out var line))
            {
                line.UnitPrice = change.NewPrice;
            }
        }

        MessageBox.Show(
            this,
            $"Đã áp dụng điều chỉnh đơn giá.\nTổng tiền mới: {_viewModel.TotalAmount:N0} đ.\n\nKiểm tra lại rồi bấm Lưu.",
            "Đã điều chỉnh",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Closing sẽ hỏi lưu nếu còn thay đổi.
        DialogResult = false;
    }

    private async void ImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var filePath = dialog.FileName;
        try
        {
            ImportResult<TransactionLineInput>? result = null;
            BusyOverlay.Message = "Đang import Excel phiếu nhập...";
            BusyOverlay.Visibility = Visibility.Visible;
            await Task.Yield();
            await Task.Run(() => result = _excelImportService.ImportReceiptLines(filePath));

            if (result is null)
            {
                return;
            }

            if (!result.IsValid)
            {
                var message = string.Join(Environment.NewLine, result.Errors.Select(x => $"Dòng {x.RowNumber}: {x.Message}"));
                MessageBox.Show(this, message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _viewModel.ReplaceLinesFromImport(result.Data);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ExceptionMessageFormatter.Format(ex, filePath),
                "Thông báo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectAllLines_OnChanged(object sender, RoutedEventArgs e)
    {
        var isSelected = sender is CheckBox { IsChecked: true };
        foreach (var line in _viewModel.Lines)
        {
            line.IsSelected = isSelected;
        }
    }
}
