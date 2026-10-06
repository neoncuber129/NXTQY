using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inventory.App.Helpers;
using Inventory.Domain.Models;
using Inventory.Domain.Services;

namespace Inventory.App;

public sealed partial class ReceiptDialogViewModel : ObservableObject
{
    private readonly Dictionary<int, Item> _itemById;
    private readonly Dictionary<string, Item> _itemByCode;
    private bool _isSuggestingItemCode;
    public ObservableCollection<Item> Items { get; }
    public ObservableCollection<LineInputViewModel> Lines { get; } = [];
    public HashSet<string> InitialItemCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEditMode { get; }

    [ObservableProperty] private string dialogTitle = "Tạo phiếu nhập";
    [ObservableProperty] private string receiptNumber = string.Empty;
    [ObservableProperty] private DateTime receiptDate = DateTime.Today;
    [ObservableProperty] private string deliveredBy = string.Empty;
    [ObservableProperty] private string deliveredUnit = string.Empty;
    [ObservableProperty] private string receivedBy = string.Empty;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private decimal totalAmount;
    [ObservableProperty] private decimal upperLevelAmount;
    [ObservableProperty] private decimal selfProcuredAmount;
    [ObservableProperty] private string totalAmountText = "Không đồng";
    [ObservableProperty] private LineInputViewModel? selectedLine;

    public ReceiptDialogViewModel(IReadOnlyCollection<Item> items, Receipt? existing)
    {
        PropertyChanged += OnOwnPropertyChanged;
        Items = new ObservableCollection<Item>(items.OrderBy(x => x.ItemCode));
        _itemById = items.ToDictionary(x => x.Id);
        _itemByCode = items.ToDictionary(x => x.ItemCode, StringComparer.OrdinalIgnoreCase);
        IsEditMode = existing is not null;
        if (IsEditMode)
        {
            DialogTitle = "Sửa phiếu nhập";
            ReceiptNumber = existing!.ReceiptNumber;
            ReceiptDate = existing.ReceiptDate;
            DeliveredBy = existing.DeliveredBy;
            DeliveredUnit = existing.DeliveredUnit;
            ReceivedBy = existing.ReceivedBy;
            Reason = existing.Reason;
            foreach (var line in existing.Lines)
            {
                var lineVm = new LineInputViewModel
                {
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Note = line.Note
                };
                if (_itemById.TryGetValue(line.ItemId, out var item))
                {
                    // Giữ đơn giá trên phiếu; điền đủ mã/tên/danh mục/ĐVT/loại từ danh mục.
                    lineVm.BindToCatalogItem(item, overwriteUnitPrice: false);
                    lineVm.UnitPrice = line.UnitPrice;
                    InitialItemCodes.Add(item.ItemCode);
                }

                AddLineInternal(lineVm);
            }
        }

        RecalculateTotals();
        CaptureBaseline();
    }

    public bool IsDirty => _baselineSignature is not null && !string.Equals(_baselineSignature, BuildDirtySignature(), StringComparison.Ordinal);
    public bool HasUnsavedChanges => IsDirty;

    public void CaptureBaseline()
    {
        _baselineSignature = BuildDirtySignature();
        NotifyDirtyState();
    }

    public void AcceptChanges() => CaptureBaseline();

    private string? _baselineSignature;

    private void OnOwnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IsDirty) and not nameof(HasUnsavedChanges))
        {
            NotifyDirtyState();
        }
    }

    private void NotifyDirtyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private string BuildDirtySignature()
    {
        var lines = string.Join("|", Lines.Select(l =>
            string.Join('\u001f',
                l.ItemCode?.Trim() ?? string.Empty,
                l.ItemName?.Trim() ?? string.Empty,
                l.Category?.Trim() ?? string.Empty,
                l.Unit?.Trim() ?? string.Empty,
                l.UsageType?.Trim() ?? string.Empty,
                (l.Quantity ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (l.UnitPrice ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                l.Note?.Trim() ?? string.Empty)));

        return string.Join('\u001f',
            ReceiptNumber?.Trim() ?? string.Empty,
            ReceiptDate.Date.ToString("yyyy-MM-dd"),
            DeliveredBy?.Trim() ?? string.Empty,
            DeliveredUnit?.Trim() ?? string.Empty,
            ReceivedBy?.Trim() ?? string.Empty,
            Reason?.Trim() ?? string.Empty,
            lines);
    }

    [RelayCommand]
    private void AddLine()
    {
        AddLineInternal(new LineInputViewModel());
    }

    [RelayCommand]
    private void RemoveSelectedLines()
    {
        var selected = Lines.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var line in selected)
        {
            line.ValueChanged -= OnLineValueChanged;
            line.ItemCodeChanged -= OnLineItemCodeChanged;
            Lines.Remove(line);
        }

        SelectedLine = null;
        RenumberLines();
        RecalculateTotals();
        NotifyDirtyState();
    }

    public ReceiptInput BuildInput()
    {
        return new ReceiptInput
        {
            ReceiptNumber = ReceiptNumber.Trim(),
            ReceiptDate = ReceiptDate.Date,
            DeliveredBy = DeliveredBy.Trim(),
            DeliveredUnit = DeliveredUnit.Trim(),
            ReceivedBy = ReceivedBy.Trim(),
            Reason = Reason.Trim(),
            Lines = Lines.Select(ToTransactionLine).ToList()
        };
    }

    public bool Validate(out string message)
    {
        if (string.IsNullOrWhiteSpace(ReceiptNumber)
            || string.IsNullOrWhiteSpace(DeliveredBy)
            || string.IsNullOrWhiteSpace(DeliveredUnit)
            || string.IsNullOrWhiteSpace(ReceivedBy)
            || string.IsNullOrWhiteSpace(Reason))
        {
            message = "Thông tin đầu phiếu nhập chưa đầy đủ.";
            return false;
        }

        if (Lines.Count == 0)
        {
            message = "Phiếu nhập phải có ít nhất 1 dòng.";
            return false;
        }

        if (Lines.Count == 0)
        {
            message = "Phiếu nhập phải có ít nhất 1 dòng.";
            return false;
        }

        foreach (var line in Lines)
        {
            var hasIdentity = !string.IsNullOrWhiteSpace(line.ItemCode) || !string.IsNullOrWhiteSpace(line.ItemName);
            if (!hasIdentity || !line.Quantity.HasValue || line.Quantity.Value <= 0)
            {
                message = "Mỗi dòng cần tên/mã mặt hàng và số lượng > 0.";
                return false;
            }

            if (!line.UnitPrice.HasValue || line.UnitPrice.Value < 0)
            {
                var label = string.IsNullOrWhiteSpace(line.ItemName) ? line.ItemCode : line.ItemName;
                message = $"Dòng '{label}' cần đơn giá hợp lệ (để tự tạo mã nếu mặt hàng mới).";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    public void ReplaceLinesFromImport(IEnumerable<TransactionLineInput> lines)
    {
        foreach (var line in Lines)
        {
            line.ValueChanged -= OnLineValueChanged;
            line.ItemCodeChanged -= OnLineItemCodeChanged;
        }

        Lines.Clear();
        foreach (var line in lines)
        {
            AddLineInternal(new LineInputViewModel(line));
        }

        RecalculateTotals();
        NotifyDirtyState();
    }

    private void AddLineInternal(LineInputViewModel line)
    {
        var code = line.ItemCode?.Trim();
        if (!string.IsNullOrEmpty(code) && _itemByCode.TryGetValue(code, out var item))
        {
            // Mở sửa / chỉ có mã: điền metadata từ danh mục.
            if (string.IsNullOrWhiteSpace(line.ItemName))
            {
                line.BindToCatalogItem(item, overwriteUnitPrice: false);
            }
            // Excel/dòng đã có tên: chỉ gắn mã khi khớp đủ biến thể; không thì bỏ mã để lúc lưu hỏi.
            else if (line.UnitPrice is >= 0
                && ItemVariantKey.Matches(item, line.ItemName, line.UnitPrice.Value, line.Category, line.UsageType))
            {
                line.BindToCatalogItem(item, overwriteUnitPrice: false);
            }
            else
            {
                line.ItemCode = string.Empty;
                line.IsCatalogBound = false;
                line.ForceCreateNewVariant = false;
            }
        }

        line.ValueChanged += OnLineValueChanged;
        line.ItemCodeChanged += OnLineItemCodeChanged;
        Lines.Add(line);
        RenumberLines();
        NotifyDirtyState();
    }

    private void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].RowNumber = i + 1;
        }
    }

    private void OnLineItemCodeChanged(object? sender, EventArgs e)
    {
        if (_isSuggestingItemCode || sender is not LineInputViewModel line)
        {
            return;
        }

        var code = line.ItemCode?.Trim();
        if (!string.IsNullOrEmpty(code) && _itemByCode.TryGetValue(code, out var item))
        {
            line.BindToCatalogItem(item);
        }
        else if (!string.IsNullOrWhiteSpace(code))
        {
            line.IsCatalogBound = false;
            // Mặt hàng mới: gợi ý tên/đơn giá nếu gõ dạng "Tên:ĐơnGiá"
            var trimmed = code;
            var colon = trimmed.LastIndexOf(':');
            if (colon > 0 && colon < trimmed.Length - 1
                && decimal.TryParse(trimmed[(colon + 1)..].Trim(), out var parsedPrice)
                && parsedPrice >= 0)
            {
                if (string.IsNullOrWhiteSpace(line.ItemName))
                {
                    line.ItemName = trimmed[..colon].Trim();
                }

                line.UnitPrice ??= parsedPrice;
            }
            else if (string.IsNullOrWhiteSpace(line.ItemName))
            {
                line.ItemName = trimmed;
            }

            SuggestAutoItemCode(line);
        }
        else
        {
            line.IsCatalogBound = false;
        }

        RecalculateTotals();
    }

    private void OnLineValueChanged(object? sender, EventArgs e)
    {
        if (sender is LineInputViewModel line)
        {
            SuggestAutoItemCode(line);
        }

        RecalculateTotals();
    }

    /// <summary>
    /// Gợi ý mã: chỉ gắn mã danh mục khi khớp đủ tên+giá+danh mục+loại (1 biến thể).
    /// Trùng tên+giá nhưng khác biến thể → để hộp thoại lúc lưu xử lý.
    /// </summary>
    private void SuggestAutoItemCode(LineInputViewModel line)
    {
        if (_isSuggestingItemCode || line.ForceCreateNewVariant)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(line.ItemName) || !line.UnitPrice.HasValue || line.UnitPrice.Value < 0)
        {
            return;
        }

        var resolution = ItemVariantResolver.Resolve(
            Items,
            line.ItemCode,
            line.ItemName,
            line.UnitPrice,
            line.Category,
            line.UsageType,
            line.ForceCreateNewVariant);

        if (resolution.Kind is ItemVariantResolutionKind.ExactMatch or ItemVariantResolutionKind.BoundByCode
            && resolution.ExactItem is not null)
        {
            if (!line.ItemCode.Equals(resolution.ExactItem.ItemCode, StringComparison.OrdinalIgnoreCase))
            {
                _isSuggestingItemCode = true;
                try
                {
                    line.BindToCatalogItem(resolution.ExactItem);
                }
                finally
                {
                    _isSuggestingItemCode = false;
                }
            }
            else
            {
                line.IsCatalogBound = true;
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(line.ItemCode) && _itemByCode.ContainsKey(line.ItemCode.Trim()))
        {
            return;
        }

        if (resolution.Kind == ItemVariantResolutionKind.Ambiguous)
        {
            return;
        }

        var name = line.ItemName.Trim();
        var price = line.UnitPrice.Value;
        var suggested = $"{name}:{decimal.Round(price, 0, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        if (suggested.Length > 50)
        {
            suggested = suggested[..50];
        }

        var current = line.ItemCode?.Trim() ?? string.Empty;
        var canReplace = string.IsNullOrWhiteSpace(current)
            || current.Equals(name, StringComparison.OrdinalIgnoreCase)
            || IsPriorAutoSuggestion(current, name);

        if (canReplace && !current.Equals(suggested, StringComparison.Ordinal))
        {
            _isSuggestingItemCode = true;
            try
            {
                line.ItemCode = suggested;
                line.IsCatalogBound = false;
            }
            finally
            {
                _isSuggestingItemCode = false;
            }
        }
    }

    private static bool IsPriorAutoSuggestion(string currentCode, string itemName)
    {
        var prefix = itemName.Trim() + ":";
        return currentCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void RecalculateTotals()
    {
        TotalAmount = Lines.Sum(x => (x.Quantity ?? 0m) * (x.UnitPrice ?? 0m));
        UpperLevelAmount = Lines
            .Where(x => IsUpperLevelType(x.UsageType))
            .Sum(x => (x.Quantity ?? 0m) * (x.UnitPrice ?? 0m));
        SelfProcuredAmount = TotalAmount - UpperLevelAmount;
        TotalAmountText = VietnameseMoneyTextConverter.Convert(TotalAmount);
    }

    private static bool IsUpperLevelType(string? usageType)
    {
        if (string.IsNullOrWhiteSpace(usageType))
        {
            return false;
        }

        var normalized = usageType.Trim();
        return normalized.Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("trên cap", StringComparison.OrdinalIgnoreCase);
    }

    private static TransactionLineInput ToTransactionLine(LineInputViewModel line) => new()
    {
        ItemCode = line.ItemCode.Trim(),
        ItemName = line.ItemName.Trim(),
        Category = line.Category.Trim(),
        Unit = line.Unit.Trim(),
        UsageType = line.UsageType.Trim(),
        Quantity = line.Quantity ?? 0m,
        UnitPrice = line.UnitPrice,
        Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()
    };
}

public sealed partial class IssueDialogViewModel : ObservableObject
{
    private readonly Dictionary<int, Item> _itemById;
    private readonly Dictionary<string, Item> _itemByCode;
    private readonly IReadOnlyList<SubordinateUnit> _subordinateUnits;
    private readonly Func<IReadOnlyList<string>, DateTime, Task<IReadOnlyDictionary<string, decimal>>> _availabilityBatchResolver;
    private readonly Func<string, DateTime, Task<decimal>> _availabilityResolver;
    private readonly Func<int, Task<Receipt?>> _receiptResolver;
    public ObservableCollection<Item> Items { get; }
    public ObservableCollection<IssueSelectableItemViewModel> SelectableItems { get; }
    public ObservableCollection<IssueReceiptSuggestionViewModel> ReceiptSuggestions { get; }
    public ObservableCollection<LineInputViewModel> Lines { get; } = [];
    public bool IsEditMode { get; }
    public bool IsModeB => UiPreferenceService.LoadIsModeB();
    public bool HasSubordinateUnits => _subordinateUnits.Count > 0;
    public IReadOnlyList<SubordinateUnit> SubordinateUnits => _subordinateUnits;

    [ObservableProperty] private string dialogTitle = "Tạo phiếu cân đối";
    [ObservableProperty] private string issueNumber = string.Empty;
    [ObservableProperty] private DateTime issueDate = DateTime.Today;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private decimal totalAmount;
    [ObservableProperty] private decimal upperLevelAmount;
    [ObservableProperty] private decimal selfProcuredAmount;
    [ObservableProperty] private string totalAmountText = "Không đồng";
    [ObservableProperty] private LineInputViewModel? selectedLine;
    [ObservableProperty] private bool filterOutZeroStockWhenSuggesting = true;

    public IssueDialogViewModel(
        IReadOnlyCollection<Item> items,
        IReadOnlyCollection<Receipt> receipts,
        IReadOnlyCollection<SubordinateUnit> subordinateUnits,
        Issue? existing,
        Func<IReadOnlyList<string>, DateTime, Task<IReadOnlyDictionary<string, decimal>>> availabilityBatchResolver,
        Func<string, DateTime, Task<decimal>> availabilityResolver,
        Func<int, Task<Receipt?>> receiptResolver)
    {
        PropertyChanged += OnOwnPropertyChanged;
        _subordinateUnits = subordinateUnits.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();
        _availabilityBatchResolver = availabilityBatchResolver;
        _availabilityResolver = availabilityResolver;
        _receiptResolver = receiptResolver;
        Items = new ObservableCollection<Item>(items.OrderBy(x => x.ItemCode));
        SelectableItems = new ObservableCollection<IssueSelectableItemViewModel>(
            items.OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .ThenBy(x => x.ItemCode)
                .Select(x => new IssueSelectableItemViewModel(x)));
        ReceiptSuggestions = new ObservableCollection<IssueReceiptSuggestionViewModel>(
            receipts.OrderByDescending(x => x.ReceiptDate).ThenByDescending(x => x.Id).Select(x => new IssueReceiptSuggestionViewModel(x)));
        _itemById = items.ToDictionary(x => x.Id);
        _itemByCode = items.ToDictionary(x => x.ItemCode, StringComparer.OrdinalIgnoreCase);
        IsEditMode = existing is not null;
        if (IsEditMode)
        {
            DialogTitle = "Sửa phiếu cân đối";
            IssueNumber = existing!.IssueNumber;
            IssueDate = existing.IssueDate;
            Reason = existing.Reason;
            foreach (var line in existing.Lines)
            {
                var lineVm = new LineInputViewModel
                {
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Note = line.Note
                };
                if (_itemById.TryGetValue(line.ItemId, out var item))
                {
                    lineVm.BindToCatalogItem(item, overwriteUnitPrice: false);
                    lineVm.UnitPrice = line.UnitPrice;
                }

                if (HasSubordinateUnits)
                {
                    var existingAllocations = line.Allocations.ToDictionary(x => x.SubordinateUnitId, x => x.Quantity);
                    lineVm.InitializeUnitAllocations(_subordinateUnits, existingAllocations);
                }

                AddLineInternal(lineVm);
            }
        }

        _ = RefreshAvailabilityAsync();
        RecalculateTotals();
        CaptureBaseline();
    }

    public Task EnsureAvailabilityLoadedAsync() => RefreshAvailabilityAsync();

    public bool IsDirty => _baselineSignature is not null && !string.Equals(_baselineSignature, BuildDirtySignature(), StringComparison.Ordinal);
    public bool HasUnsavedChanges => IsDirty;

    public void CaptureBaseline()
    {
        _baselineSignature = BuildDirtySignature();
        NotifyDirtyState();
    }

    public void AcceptChanges() => CaptureBaseline();

    private string? _baselineSignature;

    private void OnOwnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IsDirty) and not nameof(HasUnsavedChanges))
        {
            NotifyDirtyState();
        }
    }

    private void NotifyDirtyState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private string BuildDirtySignature()
    {
        var lines = string.Join("|", Lines.Select(l =>
        {
            var allocations = string.Join(",",
                l.UnitAllocations.Select(a =>
                    $"{a.UnitId}:{(a.Quantity ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
            return string.Join('\u001f',
                l.ItemCode?.Trim() ?? string.Empty,
                l.ItemName?.Trim() ?? string.Empty,
                (l.Quantity ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (l.UnitPrice ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                l.Note?.Trim() ?? string.Empty,
                allocations);
        }));

        return string.Join('\u001f',
            IssueNumber?.Trim() ?? string.Empty,
            IssueDate.Date.ToString("yyyy-MM-dd"),
            Reason?.Trim() ?? string.Empty,
            lines);
    }

    public Task<Receipt?> ResolveReceiptAsync(int receiptId) => _receiptResolver(receiptId);

    public Task<decimal> ResolveAvailabilityAsync(string itemCode, DateTime atDate) =>
        _availabilityResolver(itemCode, atDate);

    [RelayCommand]
    private void AddLine()
    {
        var line = new LineInputViewModel();
        AddLineInternal(line);
        _ = UpdateLineAvailabilityAsync(line);
    }

    [RelayCommand]
    private async Task AddCheckedItemsAsync()
    {
        await AddItemsFromSelectionAsync(SelectableItems.Where(x => x.IsSelected).ToList());
    }

    public async Task AddItemsFromSelectionAsync(IReadOnlyList<IssueSelectableItemViewModel> selectedItems)
    {
        if (selectedItems.Count == 0)
        {
            return;
        }

        foreach (var selected in selectedItems)
        {
            if (Lines.Any(x => x.ItemCode.Equals(selected.ItemCode, StringComparison.OrdinalIgnoreCase)))
            {
                selected.IsSelected = false;
                continue;
            }

            var line = new LineInputViewModel
            {
                ItemCode = selected.ItemCode,
                ItemName = selected.ItemName,
                Category = selected.Category,
                Unit = selected.Unit,
                UsageType = selected.UsageType,
                UnitPrice = selected.UnitPrice,
                Quantity = null,
                Note = selected.Note
            };
            AddLineInternal(line);
            await UpdateLineAvailabilityAsync(line);
            selected.IsSelected = false;
            selected.Note = string.Empty;
        }

        RecalculateTotals();
    }

    [RelayCommand]
    private void SelectAllReceiptSuggestions()
    {
        foreach (var receipt in ReceiptSuggestions)
        {
            receipt.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearAllReceiptSuggestions()
    {
        foreach (var receipt in ReceiptSuggestions)
        {
            receipt.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task SuggestFromSelectedReceiptsAsync()
    {
        var selected = ReceiptSuggestions.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var receiptTasks = selected.Select(x => _receiptResolver(x.ReceiptId)).ToList();
        var receiptDetails = await Task.WhenAll(receiptTasks);

        var existingCodes = Lines
            .Select(x => x.ItemCode.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateCodes = new List<string>();
        foreach (var detail in receiptDetails)
        {
            if (detail is null)
            {
                continue;
            }

            foreach (var line in detail.Lines)
            {
                if (_itemById.TryGetValue(line.ItemId, out var item)
                    && !existingCodes.Contains(item.ItemCode))
                {
                    candidateCodes.Add(item.ItemCode);
                }
            }
        }

        IReadOnlyDictionary<string, decimal> availableByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (FilterOutZeroStockWhenSuggesting && candidateCodes.Count > 0)
        {
            availableByCode = await _availabilityBatchResolver(candidateCodes, IssueDate);
        }

        foreach (var detail in receiptDetails)
        {
            if (detail is null)
            {
                continue;
            }

            foreach (var line in detail.Lines)
            {
                if (!_itemById.TryGetValue(line.ItemId, out var item))
                {
                    continue;
                }

                if (existingCodes.Contains(item.ItemCode))
                {
                    continue;
                }

                if (FilterOutZeroStockWhenSuggesting)
                {
                    if (!availableByCode.TryGetValue(item.ItemCode, out var availableStock) || availableStock <= 0)
                    {
                        continue;
                    }
                }

                var newLine = new LineInputViewModel
                {
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Category = item.Category,
                    Unit = item.Unit,
                    UsageType = item.UsageType,
                    UnitPrice = item.UnitPrice,
                    Quantity = null,
                    Note = line.Note
                };
                AddLineInternal(newLine);
                existingCodes.Add(item.ItemCode);
            }
        }

        await RefreshAvailabilityAsync();
        RecalculateTotals();
    }

    [RelayCommand]
    private async Task RemoveSelectedLinesAsync()
    {
        var selected = Lines.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        foreach (var line in selected)
        {
            line.ValueChanged -= OnLineValueChanged;
            line.ItemCodeChanged -= OnLineItemCodeChanged;
            Lines.Remove(line);
        }

        SelectedLine = null;
        RenumberLines();
        RecalculateTotals();
        await RefreshAvailabilityAsync();
        NotifyDirtyState();
    }

    public IssueInput BuildInput()
    {
        return new IssueInput
        {
            IssueNumber = IssueNumber.Trim(),
            IssueDate = IssueDate.Date,
            ReceivedBy = string.Empty,
            ReceivedUnit = string.Empty,
            DeliveredBy = string.Empty,
            Reason = Reason.Trim(),
            Lines = Lines.Select(ToTransactionLine).ToList()
        };
    }

    public bool Validate(out string message)
    {
        if (string.IsNullOrWhiteSpace(IssueNumber))
        {
            message = "Vui lòng nhập tên / số phiếu cân đối.";
            return false;
        }

        if (Lines.Count == 0)
        {
            message = "Phiếu cân đối phải có ít nhất 1 dòng.";
            return false;
        }

        if (Lines.Any(x => string.IsNullOrWhiteSpace(x.ItemCode)))
        {
            message = "Mỗi dòng phiếu cân đối phải có mã mặt hàng.";
            return false;
        }

        if (Lines.Any(x => !x.Quantity.HasValue || x.Quantity.Value < 0))
        {
            message = "Mỗi dòng phiếu cân đối phải có số lượng ≥ 0.";
            return false;
        }

        var insufficientRows = Lines.Where(x => x.HasInsufficientStock).Select(x => x.RowNumber).ToList();
        if (insufficientRows.Count > 0)
        {
            message = $"Số lượng xuất vượt tồn tại dòng: {string.Join(", ", insufficientRows)}.";
            return false;
        }

        if (HasSubordinateUnits)
        {
            foreach (var line in Lines)
            {
                if (line.UnitAllocations.Count == 0)
                {
                    continue;
                }

                var allocationSum = line.UnitAllocations.Sum(x => x.Quantity ?? 0m);
                var lineQty = line.Quantity ?? 0m;
                // SL = 0: vẫn giữ dòng trên phiếu cân đối; không bắt buộc phân bổ đơn vị.
                if (lineQty == 0)
                {
                    if (allocationSum != 0)
                    {
                        message = $"Mặt hàng {line.ItemCode}: số lượng dòng = 0 thì phân bổ đơn vị cũng phải = 0.";
                        return false;
                    }

                    continue;
                }

                if (allocationSum <= 0)
                {
                    message = $"Mặt hàng {line.ItemCode}: vui lòng nhập số lượng cho ít nhất một đơn vị trực thuộc.";
                    return false;
                }

                if (allocationSum != lineQty)
                {
                    message = $"Mặt hàng {line.ItemCode}: tổng phân bổ đơn vị ({allocationSum:N0}) không khớp số lượng dòng ({lineQty:N0}).";
                    return false;
                }
            }
        }

        message = string.Empty;
        return true;
    }

    public async Task ReplaceLinesFromImportAsync(IEnumerable<TransactionLineInput> lines)
    {
        foreach (var line in Lines)
        {
            line.ValueChanged -= OnLineValueChanged;
            line.ItemCodeChanged -= OnLineItemCodeChanged;
        }

        Lines.Clear();
        foreach (var line in lines)
        {
            var lineVm = new LineInputViewModel(line);
            if (HasSubordinateUnits && lineVm.UnitAllocations.Count == 0)
            {
                lineVm.InitializeUnitAllocations(_subordinateUnits, line.Allocations);
            }

            AddLineInternal(lineVm);
        }

        RenumberLines();
        await RefreshAvailabilityAsync();
        RecalculateTotals();
        NotifyDirtyState();
    }

    /// <summary>
    /// Xử lý dán / nhập dữ liệu Excel ở Chế độ B:
    /// - Gom / cộng gộp các dòng trùng mã hàng (tính tổng số lượng & phân bổ đơn vị)
    /// - Kiểm tra mã hàng không tồn tại trong danh mục và thông báo danh sách mã bị bỏ qua
    /// - Cập nhật số lượng cho các dòng đã có hoặc thêm dòng mới cho mã hàng hợp lệ
    /// </summary>
    public async Task<(int UpdatedCount, int ConsolidatedCount, List<string> UnknownCodes)> ApplyModeBImportAsync(
        IEnumerable<TransactionLineInput> imported)
    {
        var importedList = imported.ToList();

        // 1. Gom nhóm & Cộng gộp các mã trùng
        var itemGroups = new Dictionary<string, (string DisplayCode, decimal TotalQty, Dictionary<int, decimal> Allocations, List<string> Notes)>(StringComparer.OrdinalIgnoreCase);
        var consolidatedCount = 0;

        foreach (var line in importedList)
        {
            var code = line.ItemCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                if (!string.IsNullOrWhiteSpace(line.ItemName) && _itemByCode.TryGetValue(line.ItemName.Trim(), out var matchedItem))
                {
                    code = matchedItem.ItemCode;
                }
                else
                {
                    continue;
                }
            }

            if (!itemGroups.TryGetValue(code, out var group))
            {
                group = (code, 0m, new Dictionary<int, decimal>(), new List<string>());
            }
            else
            {
                consolidatedCount++;
            }

            var newTotalQty = group.TotalQty + line.Quantity;
            var newAllocations = group.Allocations;

            if (line.Allocations is not null)
            {
                foreach (var (unitId, qty) in line.Allocations)
                {
                    if (qty > 0)
                    {
                        newAllocations[unitId] = newAllocations.GetValueOrDefault(unitId) + qty;
                    }
                }
            }

            var newNotes = group.Notes;
            if (!string.IsNullOrWhiteSpace(line.Note))
            {
                newNotes.Add(line.Note.Trim());
            }

            itemGroups[code] = (group.DisplayCode, newTotalQty, newAllocations, newNotes);
        }

        // 2. Phân loại mã hàng hợp lệ trong danh mục và mã hàng chưa có
        var unknownCodes = new List<string>();
        var validItemsToApply = new List<(Item CatalogItem, decimal TotalQty, Dictionary<int, decimal> Allocations, string? Note)>();

        foreach (var (code, group) in itemGroups)
        {
            if (_itemByCode.TryGetValue(code, out var catalogItem))
            {
                var combinedNote = group.Notes.Count > 0 ? string.Join("; ", group.Notes.Distinct()) : null;
                validItemsToApply.Add((catalogItem, group.TotalQty, group.Allocations, combinedNote));
            }
            else
            {
                unknownCodes.Add(code);
            }
        }

        // 3. Cập nhật các dòng hàng trên form
        var updatedCount = 0;
        foreach (var (catalogItem, totalQty, allocations, note) in validItemsToApply)
        {
            var existingLine = Lines.FirstOrDefault(x => x.ItemCode != null && x.ItemCode.Equals(catalogItem.ItemCode, StringComparison.OrdinalIgnoreCase));
            if (existingLine is not null)
            {
                if (HasSubordinateUnits && allocations.Count > 0)
                {
                    if (existingLine.UnitAllocations.Count == 0)
                    {
                        existingLine.InitializeUnitAllocations(_subordinateUnits, allocations);
                    }
                    else
                    {
                        foreach (var alloc in existingLine.UnitAllocations)
                        {
                            alloc.Quantity = allocations.TryGetValue(alloc.UnitId, out var q) && q > 0 ? q : null;
                        }
                    }
                }
                else
                {
                    existingLine.Quantity = totalQty >= 0 ? totalQty : null;
                }

                if (!string.IsNullOrWhiteSpace(note))
                {
                    existingLine.Note = note;
                }

                updatedCount++;
            }
            else
            {
                var lineVm = new LineInputViewModel
                {
                    ItemCode = catalogItem.ItemCode,
                    ItemName = catalogItem.ItemName,
                    Category = catalogItem.Category,
                    Unit = catalogItem.Unit,
                    UsageType = catalogItem.UsageType,
                    UnitPrice = catalogItem.UnitPrice,
                    Quantity = totalQty >= 0 ? totalQty : null,
                    Note = note ?? string.Empty
                };

                if (HasSubordinateUnits)
                {
                    lineVm.InitializeUnitAllocations(_subordinateUnits, allocations);
                }

                AddLineInternal(lineVm);
                updatedCount++;
            }
        }

        RenumberLines();
        await RefreshAvailabilityAsync();
        RecalculateTotals();
        NotifyDirtyState();

        return (updatedCount, consolidatedCount, unknownCodes);
    }

    /// <summary>
    /// Đồng bộ phân bổ / số lượng từ Excel: chỉ cập nhật dòng đã có trên form.
    /// File mới (cột DanhMục): khớp theo thứ tự dòng. File cũ (cột MãHàng): khớp theo mã.
    /// </summary>
    public async Task<(int Updated, int SkippedUnknown)> ApplyUnitAllocationsFromImportAsync(
        IEnumerable<TransactionLineInput> imported)
    {
        var importedList = imported.ToList();
        var matchByItemCode = importedList.Any(x => !string.IsNullOrWhiteSpace(x.ItemCode));

        if (matchByItemCode)
        {
            return await ApplyUnitAllocationsByItemCodeAsync(importedList);
        }

        return await ApplyUnitAllocationsByRowOrderAsync(importedList);
    }

    private async Task<(int Updated, int SkippedUnknown)> ApplyUnitAllocationsByItemCodeAsync(
        IReadOnlyList<TransactionLineInput> importedList)
    {
        var byCode = new Dictionary<string, TransactionLineInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in importedList)
        {
            var code = line.ItemCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            byCode[code] = line;
        }

        var knownCodes = Lines
            .Select(x => x.ItemCode?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skippedUnknown = byCode.Keys.Count(code => !knownCodes.Contains(code));
        var updated = 0;

        foreach (var line in Lines)
        {
            var code = line.ItemCode?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !byCode.TryGetValue(code, out var source))
            {
                continue;
            }

            ApplyImportedQuantities(line, source);
            updated++;
        }

        await RefreshAvailabilityAsync();
        RecalculateTotals();
        NotifyDirtyState();
        return (updated, skippedUnknown);
    }

    private async Task<(int Updated, int SkippedUnknown)> ApplyUnitAllocationsByRowOrderAsync(
        IReadOnlyList<TransactionLineInput> importedList)
    {
        var updated = Math.Min(Lines.Count, importedList.Count);
        for (var i = 0; i < updated; i++)
        {
            ApplyImportedQuantities(Lines[i], importedList[i]);
        }

        var skippedUnknown = Math.Max(0, importedList.Count - Lines.Count);
        await RefreshAvailabilityAsync();
        RecalculateTotals();
        NotifyDirtyState();
        return (updated, skippedUnknown);
    }

    private void ApplyImportedQuantities(LineInputViewModel line, TransactionLineInput source)
    {
        if (HasSubordinateUnits)
        {
            if (line.UnitAllocations.Count == 0)
            {
                line.InitializeUnitAllocations(_subordinateUnits, source.Allocations);
            }
            else
            {
                foreach (var allocation in line.UnitAllocations)
                {
                    allocation.Quantity = source.Allocations.TryGetValue(allocation.UnitId, out var qty) && qty > 0
                        ? qty
                        : null;
                }
            }
        }
        else
        {
            line.Quantity = source.Quantity >= 0 ? source.Quantity : null;
        }
    }

    partial void OnIssueDateChanged(DateTime value)
    {
        _ = RefreshAvailabilityAsync();
    }

    private async Task RefreshAvailabilityAsync()
    {
        var distinctCodes = Lines
            .Select(x => x.ItemCode?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (distinctCodes.Count > 0)
        {
            var batch = await _availabilityBatchResolver(distinctCodes, IssueDate);
            foreach (var pair in batch)
            {
                availableByCode[pair.Key] = pair.Value;
            }
        }

        // Hiển thị tồn dự kiến sau từng dòng. Khi người dùng sửa số lượng,
        // tồn phải thay đổi ngay (base đã cộng lại phiếu cũ trong chế độ sửa).
        var usedByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in Lines)
        {
            var code = line.ItemCode?.Trim();
            if (string.IsNullOrWhiteSpace(code) || !availableByCode.TryGetValue(code, out var baseAvailable))
            {
                line.StockAtIssueDateQuantity = 0;
                line.AvailableQuantity = -(line.Quantity ?? 0m);
                continue;
            }

            var used = usedByCode.TryGetValue(code, out var currentUsed) ? currentUsed : 0m;
            var quantity = line.Quantity ?? 0m;
            var usedIncludingCurrentLine = used + quantity;
            line.StockAtIssueDateQuantity = baseAvailable;
            line.AvailableQuantity = baseAvailable - usedIncludingCurrentLine;
            usedByCode[code] = usedIncludingCurrentLine;
        }
    }

    private async void OnLineItemCodeChanged(object? sender, EventArgs e)
    {
        if (sender is LineInputViewModel line)
        {
            var code = line.ItemCode?.Trim();
            if (!string.IsNullOrEmpty(code) && _itemByCode.TryGetValue(code, out var item))
            {
                line.ItemName = item.ItemName;
                line.Category = item.Category;
                line.Unit = item.Unit;
                line.UsageType = item.UsageType;
                line.UnitPrice ??= item.UnitPrice;
            }
            else if (string.IsNullOrWhiteSpace(line.UsageType))
            {
                line.UsageType = string.Empty;
            }

            await UpdateLineAvailabilityAsync(line);
            RecalculateTotals();
        }
    }

    private async Task UpdateLineAvailabilityAsync(LineInputViewModel line)
    {
        if (string.IsNullOrWhiteSpace(line.ItemCode))
        {
            line.StockAtIssueDateQuantity = 0;
            line.AvailableQuantity = 0;
            return;
        }

        await RefreshAvailabilityAsync();
    }

    private void AddLineInternal(LineInputViewModel line)
    {
        var code = line.ItemCode?.Trim();
        if (!string.IsNullOrEmpty(code) && _itemByCode.TryGetValue(code, out var item))
        {
            line.ItemName = item.ItemName;
            line.Category = item.Category;
            line.Unit = item.Unit;
            line.UsageType = item.UsageType;
            line.UnitPrice ??= item.UnitPrice;
        }

        if (HasSubordinateUnits && line.UnitAllocations.Count == 0)
        {
            line.InitializeUnitAllocations(_subordinateUnits);
        }

        line.ValueChanged += OnLineValueChanged;
        line.ItemCodeChanged += OnLineItemCodeChanged;
        Lines.Add(line);
        RenumberLines();
        NotifyDirtyState();
    }

    private void RenumberLines()
    {
        for (var i = 0; i < Lines.Count; i++)
        {
            Lines[i].RowNumber = i + 1;
        }
    }

    private void OnLineValueChanged(object? sender, EventArgs e)
    {
        RecalculateTotals();
        _ = RefreshAvailabilityAsync();
    }

    private void RecalculateTotals()
    {
        TotalAmount = Lines.Sum(x => (x.Quantity ?? 0m) * (x.UnitPrice ?? 0m));
        UpperLevelAmount = Lines
            .Where(x => IsUpperLevelType(x.UsageType))
            .Sum(x => (x.Quantity ?? 0m) * (x.UnitPrice ?? 0m));
        SelfProcuredAmount = TotalAmount - UpperLevelAmount;
        TotalAmountText = VietnameseMoneyTextConverter.Convert(TotalAmount);
    }

    private static bool IsUpperLevelType(string? usageType)
    {
        if (string.IsNullOrWhiteSpace(usageType))
        {
            return false;
        }

        var normalized = usageType.Trim();
        return normalized.Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("trên cap", StringComparison.OrdinalIgnoreCase);
    }

    private static TransactionLineInput ToTransactionLine(LineInputViewModel line) => new()
    {
        ItemCode = line.ItemCode.Trim(),
        ItemName = line.ItemName.Trim(),
        Category = line.Category.Trim(),
        Unit = line.Unit.Trim(),
        UsageType = line.UsageType.Trim(),
        Quantity = line.Quantity ?? 0m,
        UnitPrice = line.UnitPrice,
        Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
        Allocations = line.GetAllocationsDictionary()
    };
}

public partial class IssueSelectableItemViewModel : ObservableObject
{
    public int ItemId { get; }
    public string ItemCode { get; }
    public string ItemName { get; }
    public string Category { get; }
    public string Unit { get; }
    public string UsageType { get; }
    public decimal UnitPrice { get; }

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string note = string.Empty;

    public IssueSelectableItemViewModel(Item item)
    {
        ItemId = item.Id;
        ItemCode = item.ItemCode;
        ItemName = item.ItemName;
        Category = item.Category;
        Unit = item.Unit;
        UsageType = item.UsageType;
        UnitPrice = item.UnitPrice;
        Note = item.Note ?? string.Empty;
    }
}

public sealed partial class IssueReceiptSuggestionViewModel : ObservableObject
{
    public int ReceiptId { get; }
    public string ReceiptNumber { get; }
    public DateTime ReceiptDate { get; }
    public string DeliveredBy { get; }
    public string Reason { get; }
    public decimal TotalAmount { get; }

    [ObservableProperty] private bool isSelected;

    public IssueReceiptSuggestionViewModel(Receipt receipt)
    {
        ReceiptId = receipt.Id;
        ReceiptNumber = receipt.ReceiptNumber;
        ReceiptDate = receipt.ReceiptDate;
        DeliveredBy = receipt.DeliveredBy;
        Reason = receipt.Reason;
        TotalAmount = receipt.TotalAmount;
    }
}
