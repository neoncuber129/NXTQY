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
    private readonly DateTime _atDate;
    private readonly HashSet<string> _alreadyOnVoucher;

    public ObservableCollection<IssueSelectableItemViewModel> FullItems { get; }
    public ObservableCollection<IssueReceiptSuggestionViewModel> Receipts { get; }
    public ObservableCollection<ReceiptPickedItemViewModel> ReceiptItems { get; } = [];
    public ICollectionView FullItemsView { get; }
    public ICollectionView ReceiptItemsView { get; }

    [ObservableProperty] private bool filterOutZeroStock = true;
    [ObservableProperty] private string searchText = string.Empty;

    public ItemPickerViewModel(
        IEnumerable<IssueSelectableItemViewModel> fullItems,
        IEnumerable<IssueReceiptSuggestionViewModel> receipts,
        IReadOnlyDictionary<int, Item> itemById,
        IEnumerable<string> alreadyOnVoucher,
        DateTime atDate,
        Func<int, Task<Receipt?>> receiptResolver,
        Func<string, DateTime, Task<decimal>> availabilityResolver)
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

        FullItemsView = CollectionViewSource.GetDefaultView(FullItems);
        ReceiptItemsView = CollectionViewSource.GetDefaultView(ReceiptItems);
        FullItemsView.Filter = item => item is IssueSelectableItemViewModel row && MatchesSearch(row);
        ReceiptItemsView.Filter = item => item is ReceiptPickedItemViewModel row && MatchesSearch(row);
    }

    partial void OnSearchTextChanged(string value)
    {
        FullItemsView.Refresh();
        ReceiptItemsView.Refresh();
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

        var availableByCode = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
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

                if (_alreadyOnVoucher.Contains(item.ItemCode) || added.Contains(item.ItemCode))
                {
                    continue;
                }

                if (FilterOutZeroStock)
                {
                    if (!availableByCode.TryGetValue(item.ItemCode, out var stock))
                    {
                        stock = await _availabilityResolver(item.ItemCode, _atDate);
                        availableByCode[item.ItemCode] = stock;
                    }

                    if (stock <= 0)
                    {
                        continue;
                    }
                }

                ReceiptItems.Add(new ReceiptPickedItemViewModel(item, suggestion.ReceiptNumber, line.Note));
                added.Add(item.ItemCode);
            }
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
