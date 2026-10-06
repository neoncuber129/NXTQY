using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Inventory.Domain.Models;

namespace Inventory.App.Views;

public partial class SubordinateUnitDialogWindow : Window
{
    private readonly SubordinateUnitDialogViewModel _viewModel = new();

    public SubordinateUnitDialogWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public SubordinateUnit Unit => new()
    {
        UnitCode = _viewModel.UnitCode.Trim(),
        Name = _viewModel.UnitName.Trim(),
        ReceivedBy = _viewModel.ReceivedBy.Trim(),
        DeliveredBy = string.Empty,
        Reason = string.Empty,
        Note = string.IsNullOrWhiteSpace(_viewModel.Note) ? null : _viewModel.Note.Trim()
    };

    private void Add_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.UnitCode))
        {
            ShowValidationError("Vui lòng nhập mã đơn vị.", UnitCodeBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.UnitName))
        {
            ShowValidationError("Vui lòng nhập tên đơn vị.", UnitNameBox);
            return;
        }

        DialogResult = true;
    }

    private void ShowValidationError(string message, FrameworkElement control)
    {
        MessageBox.Show(this, message, "Thiếu thông tin",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        control.Focus();
    }
}

public sealed partial class SubordinateUnitDialogViewModel : ObservableObject
{
    [ObservableProperty] private string unitCode = string.Empty;
    [ObservableProperty] private string unitName = string.Empty;
    [ObservableProperty] private string receivedBy = string.Empty;
    [ObservableProperty] private string note = string.Empty;
}
