using System.Windows;

namespace Inventory.App.Helpers;

public static class UnsavedChangesPrompt
{
    /// <summary>
    /// Hỏi lưu trước khi thoát. true = lưu, false = bỏ qua, null = ở lại.
    /// </summary>
    public static bool? Ask(Window owner, string? detail = null)
    {
        var message = string.IsNullOrWhiteSpace(detail)
            ? "Có thay đổi chưa lưu. Bạn có muốn lưu trước khi thoát?"
            : $"Có thay đổi chưa lưu ({detail}). Bạn có muốn lưu trước khi thoát?";

        var result = MessageBox.Show(
            owner,
            message + Environment.NewLine + Environment.NewLine
            + "Yes = Lưu  |  No = Không lưu  |  Cancel = Ở lại",
            "Lưu thay đổi",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null
        };
    }
}
