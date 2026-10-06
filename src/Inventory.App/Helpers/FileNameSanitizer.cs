using System.IO;

namespace Inventory.App.Helpers;

/// <summary>
/// Làm sạch thành phần tên file: thay / \ : * ? " &lt; &gt; | và ký tự không hợp lệ khác bằng '_'.
/// </summary>
public static class FileNameSanitizer
{
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Concat(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch));
        return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
    }
}
