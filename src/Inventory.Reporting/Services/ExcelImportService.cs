using ClosedXML.Excel;
using Inventory.Domain.Models;
using System.Globalization;
using System.Text;

namespace Inventory.Reporting.Services;

public sealed class ExcelImportService
{
    public ImportResult<TransactionLineInput> ImportIssueLines(string filePath)
    {
        var result = new ImportResult<TransactionLineInput>();
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var headerMap = BuildHeaderMap(worksheet);

        var requiredHeaders = new[] { "mahang", "soluong" };
        ValidateHeaders(headerMap, requiredHeaders, result);
        if (!result.IsValid)
        {
            return result;
        }

        var row = 2;
        while (!worksheet.Row(row).IsEmpty())
        {
            try
            {
                var itemCode = worksheet.Cell(row, headerMap["mahang"]).GetString().Trim();
                var quantity = worksheet.Cell(row, headerMap["soluong"]).GetValue<decimal>();

                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "Thiếu MãHàng." });
                }
                else if (quantity <= 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "SốLượng phải > 0." });
                }
                else
                {
                    result.Data.Add(new TransactionLineInput
                    {
                        ItemCode = itemCode,
                        Quantity = quantity
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ImportError { RowNumber = row, Message = $"Lỗi đọc dữ liệu: {ex.Message}" });
            }

            row++;
        }

        return result;
    }

    public ImportResult<TransactionLineInput> ImportTransactionLines(string filePath)
    {
        var result = new ImportResult<TransactionLineInput>();
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var headerMap = BuildHeaderMap(worksheet);

        var requiredHeaders = new[] { "mahang", "soluong" };
        ValidateHeaders(headerMap, requiredHeaders, result);
        if (!result.IsValid)
        {
            return result;
        }

        var row = 2;
        while (!worksheet.Row(row).IsEmpty())
        {
            try
            {
                var itemCode = worksheet.Cell(row, headerMap["mahang"]).GetString().Trim();
                var quantity = worksheet.Cell(row, headerMap["soluong"]).GetValue<decimal>();
                decimal? unitPrice = null;
                if (headerMap.TryGetValue("dongia", out var unitPriceCol))
                {
                    var text = worksheet.Cell(row, unitPriceCol).GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        unitPrice = worksheet.Cell(row, unitPriceCol).GetValue<decimal>();
                    }
                }

                var note = headerMap.TryGetValue("ghichu", out var noteCol)
                    ? worksheet.Cell(row, noteCol).GetString().Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(itemCode))
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "Thiếu MãHàng." });
                }
                else if (quantity <= 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "SốLượng phải > 0." });
                }
                else if (unitPrice.HasValue && unitPrice.Value < 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "ĐơnGiá không được âm." });
                }
                else
                {
                    result.Data.Add(new TransactionLineInput
                    {
                        ItemCode = itemCode,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        Note = note
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ImportError { RowNumber = row, Message = $"Lỗi đọc dữ liệu: {ex.Message}" });
            }

            row++;
        }

        return result;
    }

    public ImportResult<TransactionLineInput> ImportReceiptLines(string filePath)
    {
        var result = new ImportResult<TransactionLineInput>();
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var headerMap = BuildHeaderMap(worksheet);

        var isLegacyFormat = headerMap.ContainsKey("mahang") && headerMap.ContainsKey("soluong");
        if (isLegacyFormat)
        {
            return ImportTransactionLines(filePath);
        }

        var requiredHeaders = new[] { "danhmuc", "tenmathang", "dvt", "dongia", "loaihang", "soluong" };
        // Thành tiền / Tổng tiền phiếu (nếu có) chỉ hỗ trợ xem trên Excel — không đọc khi import.
        ValidateHeaders(headerMap, requiredHeaders, result);
        if (!result.IsValid)
        {
            return result;
        }

        var row = 2;
        while (!worksheet.Row(row).IsEmpty())
        {
            try
            {
                var itemName = worksheet.Cell(row, headerMap["tenmathang"]).GetString().Trim();
                // Dòng trống trong Excel Table (chỉ còn công thức Thành tiền) — bỏ qua.
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    row++;
                    continue;
                }

                var category = worksheet.Cell(row, headerMap["danhmuc"]).GetString().Trim();
                var unit = worksheet.Cell(row, headerMap["dvt"]).GetString().Trim();
                var usageType = worksheet.Cell(row, headerMap["loaihang"]).GetString().Trim();
                var quantity = worksheet.Cell(row, headerMap["soluong"]).GetValue<decimal>();
                var unitPrice = worksheet.Cell(row, headerMap["dongia"]).GetValue<decimal>();
                var note = headerMap.TryGetValue("ghichu", out var noteCol)
                    ? worksheet.Cell(row, noteCol).GetString().Trim()
                    : null;

                if (quantity <= 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "SốLượng phải > 0." });
                }
                else if (unitPrice < 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "ĐơnGiá không được âm." });
                }
                else
                {
                    // Không gán sẵn mã Tên:Giá — tránh gắn nhầm biến thể cũ và ghi đè danh mục/loại từ Excel.
                    // Mã được chọn/sinh khi lưu (hộp thoại biến thể nếu cần).
                    result.Data.Add(new TransactionLineInput
                    {
                        ItemCode = string.Empty,
                        ItemName = itemName,
                        Category = category,
                        Unit = unit,
                        UsageType = usageType,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        Note = note
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ImportError { RowNumber = row, Message = $"Lỗi đọc dữ liệu: {ex.Message}" });
            }

            row++;
        }

        return result;
    }

    public ImportResult<OpeningBalanceInput> ImportOpeningBalances(string filePath)
    {
        var result = new ImportResult<OpeningBalanceInput>();
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheet(1);
        var headerMap = BuildHeaderMap(worksheet);

        var requiredHeaders = new[] { "danhmuc", "tenmathang", "dvt", "dongia", "loaihang", "soluongtondau" };
        // Thành tiền / Tổng tiền phiếu (nếu có) chỉ hỗ trợ xem trên Excel — không đọc khi import.
        ValidateHeaders(headerMap, requiredHeaders, result);
        if (!result.IsValid)
        {
            return result;
        }

        var row = 2;
        while (!worksheet.Row(row).IsEmpty())
        {
            try
            {
                var itemName = worksheet.Cell(row, headerMap["tenmathang"]).GetString().Trim();
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    row++;
                    continue;
                }

                var category = worksheet.Cell(row, headerMap["danhmuc"]).GetString().Trim();
                var unit = worksheet.Cell(row, headerMap["dvt"]).GetString().Trim();
                var usageType = worksheet.Cell(row, headerMap["loaihang"]).GetString().Trim();
                var quantity = worksheet.Cell(row, headerMap["soluongtondau"]).GetValue<decimal>();
                var unitPrice = worksheet.Cell(row, headerMap["dongia"]).GetValue<decimal>();
                var note = headerMap.TryGetValue("ghichu", out var noteCol)
                    ? worksheet.Cell(row, noteCol).GetString().Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(unit))
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "Thiếu ĐVT." });
                }
                else if (quantity < 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "SốLượngTồnĐầu không được âm." });
                }
                else if (unitPrice < 0)
                {
                    result.Errors.Add(new ImportError { RowNumber = row, Message = "ĐơnGiá không được âm." });
                }
                else
                {
                    result.Data.Add(new OpeningBalanceInput
                    {
                        Category = category,
                        ItemName = itemName,
                        Unit = unit,
                        UsageType = usageType,
                        OpeningQuantity = quantity,
                        UnitPrice = unitPrice,
                        Note = string.IsNullOrWhiteSpace(note) ? null : note
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ImportError { RowNumber = row, Message = $"Lỗi đọc dữ liệu: {ex.Message}" });
            }

            row++;
        }

        var duplicates = result.Data
            .GroupBy(x => $"{x.ItemName.Trim().ToUpperInvariant()}::{x.UnitPrice}")
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var key in duplicates)
        {
            var parts = key.Split("::");
            result.Errors.Add(new ImportError { RowNumber = 0, Message = $"Trùng TênMặtHàng + ĐơnGiá trong file: {parts[0]} - {parts[1]}." });
        }

        return result;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet worksheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var column = 1;
        while (!worksheet.Cell(1, column).IsEmpty())
        {
            var header = worksheet.Cell(1, column).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(header))
            {
                map[CanonicalHeader(header)] = column;
            }

            column++;
        }

        return map;
    }

    private static void ValidateHeaders(
        Dictionary<string, int> headerMap,
        IReadOnlyCollection<string> requiredHeaders,
        ImportResult<TransactionLineInput> result)
    {
        var missing = requiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add(new ImportError { RowNumber = 1, Message = $"Thiếu cột: {string.Join(", ", missing)}." });
        }
    }

    private static void ValidateHeaders(
        Dictionary<string, int> headerMap,
        IReadOnlyCollection<string> requiredHeaders,
        ImportResult<OpeningBalanceInput> result)
    {
        var missing = requiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add(new ImportError { RowNumber = 1, Message = $"Thiếu cột: {string.Join(", ", missing)}." });
        }
    }

    private static string CanonicalHeader(string header)
    {
        var normalized = header.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark || char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (ch is '_' or '-')
            {
                continue;
            }

            if (ch is 'đ' or 'Đ')
            {
                builder.Append('d');
                continue;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
