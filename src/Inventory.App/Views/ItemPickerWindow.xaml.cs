using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using Inventory.Domain.Models;

namespace Inventory.App.Views;

public partial class ItemPickerWindow : Window
{
    private readonly ItemPickerViewModel _viewModel;

    public ItemPickerWindow(ItemPickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    public IReadOnlyList<IssueSelectableItemViewModel> SelectedItems { get; private set; } = [];

    private void SelectAllFull_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _viewModel.FullItemsView.Cast<IssueSelectableItemViewModel>())
        {
            item.IsSelected = true;
        }
    }

    private void ClearAllFull_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _viewModel.FullItems)
        {
            item.IsSelected = false;
        }
    }

    private void SelectAllReceipts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var receipt in _viewModel.Receipts)
        {
            receipt.IsSelected = true;
        }
    }

    private void ClearAllReceipts_Click(object sender, RoutedEventArgs e)
    {
        foreach (var receipt in _viewModel.Receipts)
        {
            receipt.IsSelected = false;
        }
    }

    private async void LoadFromReceipts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadItemsFromSelectedReceiptsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddSelected_Click(object sender, RoutedEventArgs e)
    {
        var fromFull = _viewModel.FullItems.Where(x => x.IsSelected).ToList();
        var fromReceipt = _viewModel.ReceiptItems.Where(x => x.IsSelected).ToList();
        var merged = new Dictionary<string, IssueSelectableItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in fromFull.Concat(fromReceipt))
        {
            merged[item.ItemCode] = item;
        }

        if (merged.Count == 0)
        {
            MessageBox.Show(this, "Vui lòng tick chọn ít nhất một mặt hàng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var zeroStockItems = merged.Values.Where(x => x.AvailableStock <= 0).ToList();
        if (zeroStockItems.Count > 0)
        {
            var message = $"Cảnh báo: Có {zeroStockItems.Count} mặt hàng được chọn có tồn ngày lập phiếu <= 0 (tính đến ngày {_viewModel.AtDate:dd/MM/yyyy}):\n- "
                + string.Join("\n- ", zeroStockItems.Take(5).Select(x => $"{x.ItemCode} - {x.ItemName} (Tồn: {x.AvailableStock:N0})"))
                + (zeroStockItems.Count > 5 ? $"\n... và {zeroStockItems.Count - 5} mặt hàng khác." : "")
                + "\n\nBạn có chắc chắn muốn đưa vào phiếu cân đối không?";

            var confirm = MessageBox.Show(this, message, "Cảnh báo tồn ngày lập phiếu", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SelectedItems = merged.Values.ToList();
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void DataGrid_OnLoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }
}

public sealed partial class ItemPickerViewModel : ObservableObject
{
    private readonly Dictionary<int, Item> _itemById;
    private readonly Func<int, Task<Receipt?>> _receiptResolver;
    private readonly Func<string, DateTime, Task<decimal>> _availabilityResolver;
    private readonly Func<IReadOnlyList<string>, DateTime, Task<IReadOnlyDictionary<string, decimal>>>? _availabilityBatchResolver;
    private readonly DateTime _atDate;
    private readonly HashSet<string> _alreadyOnVoucher;

    public ObservableCollection<IssueSelectableItemViewModel> FullItems { get; }
    public ObservableCollection<IssueReceiptSuggestionViewModel> Receipts { get; }
    public ObservableCollection<ReceiptPickedItemViewModel> ReceiptItems { get; } = [];
    public ICollectionView FullItemsView { get; }
    public ICollectionView ReceiptItemsView { get; }

    public DateTime AtDate => _atDate;
    public string DateNoticeText => $"Lưu ý: Tồn ngày lập phiếu được tính đến ngày {_atDate:dd/MM/yyyy} (ngày lập phiếu đã chọn).";

    [ObservableProperty] private bool hideZeroStock = true;
    public bool FilterOutZeroStock
    {
        get => HideZeroStock;
        set => HideZeroStock = value;
    }

    [ObservableProperty] private string searchText = string.Empty;

    public ItemPickerViewModel(
        IEnumerable<IssueSelectableItemViewModel> fullItems,
        IEnumerable<IssueReceiptSuggestionViewModel> receipts,
        IReadOnlyDictionary<int, Item> itemById,
        IEnumerable<string> alreadyOnVoucher,
        DateTime atDate,
        Func<int, Task<Receipt?>> receiptResolver,
        Func<string, DateTime, Task<decimal>> availabilityResolver,
        Func<IReadOnlyList<string>, DateTime, Task<IReadOnlyDictionary<string, decimal>>>? availabilityBatchResolver = null)
    {
        FullItems = new ObservableCollection<IssueSelectableItemViewModel>(fullItems);
        Receipts = new ObservableCollection<IssueReceiptSuggestionViewModel>(receipts);
        _itemById = itemById.ToDictionary(x => x.Key, x => x.Value);
        _alreadyOnVoucher = alreadyOnVoucher
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _atDate = atDate;
        _receiptResolver = receiptResolver;
        _availabilityResolver = availabilityResolver;
        _availabilityBatchResolver = availabilityBatchResolver;

        FullItemsView = CollectionViewSource.GetDefaultView(FullItems);
        ReceiptItemsView = CollectionViewSource.GetDefaultView(ReceiptItems);

        FullItemsView.Filter = item => item is IssueSelectableItemViewModel row
            && (!HideZeroStock || row.AvailableStock > 0)
            && MatchesSearch(row);

        ReceiptItemsView.Filter = item => item is ReceiptPickedItemViewModel row
            && (!HideZeroStock || row.AvailableStock > 0)
            && MatchesSearch(row);
    }

    partial void OnHideZeroStockChanged(bool value)
    {
        OnPropertyChanged(nameof(FilterOutZeroStock));
        FullItemsView.Refresh();
        ReceiptItemsView.Refresh();
    }

    partial void OnSearchTextChanged(string value)
    {
        FullItemsView.Refresh();
        ReceiptItemsView.Refresh();
    }

    public async Task LoadAvailabilityAsync()
    {
        var codes = FullItems
            .Select(x => x.ItemCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return;
        }

        if (_availabilityBatchResolver is not null)
        {
            var dict = await _availabilityBatchResolver(codes, _atDate);
            foreach (var item in FullItems)
            {
                if (dict.TryGetValue(item.ItemCode, out var stock))
                {
                    item.AvailableStock = stock;
                }
                else
                {
                    item.AvailableStock = 0m;
                }
            }
        }
        else
        {
            foreach (var item in FullItems)
            {
                item.AvailableStock = await _availabilityResolver(item.ItemCode, _atDate);
            }
        }

        FullItemsView.Refresh();
    }

    private bool MatchesSearch(IssueSelectableItemViewModel item)
    {
        var search = SearchText.Trim();
        if (search.Length == 0)
        {
            return true;
        }

        return item.ItemCode.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Unit.Contains(search, StringComparison.OrdinalIgnoreCase)
            || item.Note.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    public async Task LoadItemsFromSelectedReceiptsAsync()
    {
        ReceiptItems.Clear();
        var selected = Receipts.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException("Vui lòng chọn ít nhất một phiếu nhập.");
        }

        var candidateItems = new List<(Item Item, string ReceiptNumber, string? Note)>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var suggestion in selected)
        {
            var detail = await _receiptResolver(suggestion.ReceiptId);
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

                if (_alreadyOnVoucher.Contains(item.ItemCode) || !added.Add(item.ItemCode))
                {
                    continue;
                }

                candidateItems.Add((item, suggestion.ReceiptNumber, line.Note));
            }
        }

        var distinctCodes = candidateItems
            .Select(x => x.Item.ItemCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyDictionary<string, decimal> stockMap;
        if (_availabilityBatchResolver is not null)
        {
            stockMap = await _availabilityBatchResolver(distinctCodes, _atDate);
        }
        else
        {
            var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in distinctCodes)
            {
                dict[code] = await _availabilityResolver(code, _atDate);
            }
            stockMap = dict;
        }

        foreach (var candidate in candidateItems)
        {
            stockMap.TryGetValue(candidate.Item.ItemCode, out var stock);
            var pickedItem = new ReceiptPickedItemViewModel(candidate.Item, candidate.ReceiptNumber, candidate.Note)
            {
                AvailableStock = stock
            };
            ReceiptItems.Add(pickedItem);
        }

        ReceiptItemsView.Refresh();
    }
}

public sealed partial class ReceiptPickedItemViewModel : IssueSelectableItemViewModel
{
    public string SourceReceiptNumber { get; }

    public ReceiptPickedItemViewModel(Item item, string sourceReceiptNumber, string? note) : base(item)
    {
        SourceReceiptNumber = sourceReceiptNumber;
        Note = note ?? string.Empty;
    }
}
