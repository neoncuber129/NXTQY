using System.IO;

namespace Inventory.App.Helpers;

public static class ExceptionMessageFormatter
{
    // Win32: ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    public static string Format(Exception ex) => Format(ex, filePath: null);

    public static string Format(Exception ex, string? filePath)
    {
        if (IsFileInUse(ex))
        {
            var name = string.IsNullOrWhiteSpace(filePath)
                ? "File"
                : Path.GetFileName(filePath);
            return $"Không ghi được «{name}» vì file đang được mở ở chương trình khác (ví dụ Excel).\n" +
                   "Hãy đóng file đó rồi xuất lại.";
        }

        var parts = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (string.IsNullOrWhiteSpace(e.Message))
            {
                continue;
            }

            if (parts.Count == 0 || !string.Equals(parts[^1], e.Message, StringComparison.Ordinal))
            {
                parts.Add(e.Message);
            }
        }

        return parts.Count == 0 ? ex.ToString() : string.Join("\n→ ", parts);
    }

    public static bool IsFileInUse(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is not IOException io)
            {
                continue;
            }

            var code = io.HResult & 0xFFFF;
            if (code is ErrorSharingViolation or ErrorLockViolation)
            {
                return true;
            }

            var message = io.Message;
            if (message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || message.Contains("đang được sử dụng", StringComparison.OrdinalIgnoreCase)
                || message.Contains("because it is being used", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
