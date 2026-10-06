using ClosedXML.Excel;
using Inventory.Domain.Models;
using System.Globalization;
using System.Text;

namespace Inventory.Reporting.Services;

/// <summary>Một dòng phiếu cân đối khi trao đổi qua file Excel tạm.</summary>
public sealed record IssueExcelLine(
    string Category,
    string ItemName,
    decimal StockAtIssueDate,
    decimal? UnitPrice,
    string? Note,
    decimal? Quantity,
    IReadOnlyDictionary<int, decimal> Allocations);

/// <summary>
/// Xuất các dòng phiếu cân đối ra file Excel tạm để người dùng nhập số lượng theo đơn vị,
/// rồi đọc ngược phân bổ về form (không thêm / đổi mặt hàng).
/// </summary>
public sealed class IssueExcelWorkspaceService
{
    private const string SheetPassword = "InventoryExcelWorkspace";
    private const string AppNumberFormat = "#,##0"; // Giống N0 trong app (vi-VN)

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "stt",
        "danhmuc",
        "mahang", // file Excel cũ
        "tenmathang",
        "tonngaylapphieu",
        "soluong",
        "soluongtong",
        "thanhtien",
        "conlai",
        "dongia",
        "ghichu",
        "tongtienphieu",
        "tong",
        "tongcong"
    };

    public void ExportWorkspace(
        string filePath,
        IReadOnlyList<IssueExcelLine> lines,
        IReadOnlyList<SubordinateUnit> units,
        bool isModeB = false)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("PhieuCanDoi");

        int colStt = 1;
        int colItemCode = isModeB ? 2 : -1;
        int colCategory = isModeB ? 3 : 2;
        int colItemName = isModeB ? 4 : 3;
        int colPrice = isModeB ? 5 : 4;
        int colNote = isModeB ? 6 : 5;
        int colStock = isModeB ? 7 : 6;
        int colQuantity = isModeB ? 8 : 7;
        int colAmount = isModeB ? 9 : 8;
        int colRemaining = isModeB ? 10 : 9;
        int unitStartColumn = isModeB ? 11 : 10;

        worksheet.Cell(1, colStt).Value = "STT";
        if (isModeB)
        {
            worksheet.Cell(1, colItemCode).Value = "MãHàng";
        }

        worksheet.Cell(1, colCategory).Value = "DanhMục";
        worksheet.Cell(1, colItemName).Value = "TênMặtHàng";
        worksheet.Cell(1, colPrice).Value = "ĐơnGiá";
        worksheet.Cell(1, colNote).Value = "GhiChú";
        worksheet.Cell(1, colStock).Value = "TồnNgàyLậpPhiếu";
        worksheet.Cell(1, colQuantity).Value = units.Count > 0 ? "SốLượng (tổng)" : "SốLượng";
        worksheet.Cell(1, colAmount).Value = "ThànhTiền";
        worksheet.Cell(1, colRemaining).Value = "CònLại";

        for (var u = 0; u < units.Count; u++)
        {
            worksheet.Cell(1, unitStartColumn + u).Value = GetUnitHeader(units[u]);
        }

        var lastColumn = units.Count > 0 ? unitStartColumn + units.Count - 1 : colRemaining;
        var headerRange = worksheet.Range(1, 1, 1, lastColumn);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xE2, 0xE8, 0xF0));
        worksheet.SheetView.FreezeRows(1);

        var computedHeader = XLColor.FromArgb(0xFE, 0xF3, 0xC7);
        worksheet.Cell(1, colStock).Style.Fill.SetBackgroundColor(computedHeader);
        worksheet.Cell(1, colQuantity).Style.Fill.SetBackgroundColor(computedHeader);
        worksheet.Cell(1, colAmount).Style.Fill.SetBackgroundColor(computedHeader);
        worksheet.Cell(1, colRemaining).Style.Fill.SetBackgroundColor(computedHeader);

        if (isModeB)
        {
            worksheet.Cell(1, colItemCode).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEC, 0xFC, 0xFF));
        }

        // Định dạng số giống app trên cả cột (kể cả dòng mới nếu có).
        ApplyAppNumberFormat(worksheet.Column(colStock));
        ApplyAppNumberFormat(worksheet.Column(colQuantity));
        ApplyAppNumberFormat(worksheet.Column(colAmount));
        ApplyAppNumberFormat(worksheet.Column(colRemaining));
        ApplyAppNumberFormat(worksheet.Column(colPrice));
        worksheet.Column(colStt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        for (var u = 0; u < units.Count; u++)
        {
            ApplyAppNumberFormat(worksheet.Column(unitStartColumn + u), center: true);
        }

        // Khóa toàn bộ sheet; chỉ mở khóa các ô nhập liệu.
        worksheet.Cells().Style.Protection.SetLocked(true);

        if (isModeB)
        {
            // Mode B: Không giới hạn số dòng. Mở khóa toàn bộ các cột nhập liệu trên toàn sheet.
            worksheet.Column(colItemCode).Style.Protection.SetLocked(false);
            worksheet.Column(colNote).Style.Protection.SetLocked(false);
            if (units.Count == 0)
            {
                worksheet.Column(colQuantity).Style.Protection.SetLocked(false);
            }
            else
            {
                for (var u = 0; u < units.Count; u++)
                {
                    worksheet.Column(unitStartColumn + u).Style.Protection.SetLocked(false);
                }
            }

            // Khóa bảo vệ dòng tiêu đề (hàng 1)
            worksheet.Row(1).Style.Protection.SetLocked(true);
        }

        var stockLetter = XLHelper.GetColumnLetterFromNumber(colStock);
        var quantityLetter = XLHelper.GetColumnLetterFromNumber(colQuantity);
        var priceLetter = XLHelper.GetColumnLetterFromNumber(colPrice);
        var amountLetter = XLHelper.GetColumnLetterFromNumber(colAmount);
        var unitFirstLetter = units.Count > 0
            ? XLHelper.GetColumnLetterFromNumber(unitStartColumn)
            : null;
        var unitLastLetter = units.Count > 0
            ? XLHelper.GetColumnLetterFromNumber(unitStartColumn + units.Count - 1)
            : null;

        var borderColor = XLColor.FromArgb(0x94, 0xA3, 0xB8);
        var templateRowCount = lines.Count > 0 ? lines.Count : (isModeB ? 100 : 0);

        for (var i = 0; i < templateRowCount; i++)
        {
            var line = i < lines.Count ? lines[i] : null;
            var row = i + 2;
            worksheet.Cell(row, colStt).Value = i + 1;

            if (line != null)
            {
                worksheet.Cell(row, colCategory).Value = line.Category;
                worksheet.Cell(row, colItemName).Value = line.ItemName;
                worksheet.Cell(row, colStock).Value = line.StockAtIssueDate;

                if (line.UnitPrice is { } price)
                {
                    worksheet.Cell(row, colPrice).Value = price;
                }

                if (!string.IsNullOrWhiteSpace(line.Note))
                {
                    worksheet.Cell(row, colNote).Value = line.Note;
                }
            }

            if (isModeB)
            {
                var codeCell = worksheet.Cell(row, colItemCode);
                codeCell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEC, 0xFC, 0xFF));
            }

            for (var u = 0; u < units.Count; u++)
            {
                var cell = worksheet.Cell(row, unitStartColumn + u);
                if (line != null && line.Allocations.TryGetValue(units[u].Id, out var allocated) && allocated > 0)
                {
                    cell.Value = allocated;
                }

                if (!isModeB)
                {
                    cell.Style.Protection.SetLocked(false);
                }
                cell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEC, 0xFC, 0xFF));
                cell.Style.NumberFormat.Format = AppNumberFormat;
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            }

            if (units.Count == 0)
            {
                var qtyCell = worksheet.Cell(row, colQuantity);
                if (line?.Quantity is { } quantity)
                {
                    qtyCell.Value = quantity;
                }

                if (!isModeB)
                {
                    qtyCell.Style.Protection.SetLocked(false);
                }
                qtyCell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xEC, 0xFC, 0xFF));
                qtyCell.Style.NumberFormat.Format = AppNumberFormat;
            }
            else
            {
                worksheet.Cell(row, colQuantity).FormulaA1 =
                    $"=IF(COUNTA({unitFirstLetter}{row}:{unitLastLetter}{row})=0,\"\",SUM({unitFirstLetter}{row}:{unitLastLetter}{row}))";
                worksheet.Cell(row, colQuantity).Style.NumberFormat.Format = AppNumberFormat;
            }

            worksheet.Cell(row, colAmount).FormulaA1 =
                $"=IF(OR({quantityLetter}{row}=\"\",{priceLetter}{row}=\"\"),\"\",{quantityLetter}{row}*{priceLetter}{row})";
            worksheet.Cell(row, colAmount).Style.NumberFormat.Format = AppNumberFormat;
            worksheet.Cell(row, colAmount).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

            worksheet.Cell(row, colRemaining).FormulaA1 =
                $"=IF(OR({stockLetter}{row}=\"\",{quantityLetter}{row}=\"\"),\"\",{stockLetter}{row}-{quantityLetter}{row})";
            worksheet.Cell(row, colRemaining).Style.NumberFormat.Format = AppNumberFormat;

            worksheet.Cell(row, colStock).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFF, 0xFB, 0xEB));
            worksheet.Cell(row, colStock).Style.NumberFormat.Format = AppNumberFormat;
            if (units.Count > 0)
            {
                worksheet.Cell(row, colQuantity).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFF, 0xFB, 0xEB));
            }

            worksheet.Cell(row, colAmount).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFF, 0xFB, 0xEB));
            worksheet.Cell(row, colRemaining).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFF, 0xFB, 0xEB));
            worksheet.Cell(row, colPrice).Style.NumberFormat.Format = AppNumberFormat;
            worksheet.Cell(row, colPrice).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

            if (i % 2 == 1)
            {
                var nonHighlightCols = isModeB
                    ? new[] { colStt, colCategory, colItemName, colPrice }
                    : new[] { colStt, colCategory, colItemName, colPrice, colNote };
                foreach (var c in nonHighlightCols)
                {
                    worksheet.Cell(row, c).Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xF8, 0xFA, 0xFC));
                }
            }

            worksheet.Cell(row, colStt).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            worksheet.Cell(row, colStock).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            worksheet.Cell(row, colQuantity).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            worksheet.Cell(row, colRemaining).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // Ô Tổng tiền phiếu cố định ở hàng 1 (frozen header) bên phải: Tiêu đề ở cột lastColumn + 1, Giá trị ở cột lastColumn + 2
        var totalHeaderCol = lastColumn + 1;
        var totalValueCol = lastColumn + 2;

        var totalHeaderCell = worksheet.Cell(1, totalHeaderCol);
        totalHeaderCell.Value = "Tổng:";
        totalHeaderCell.Style.Font.SetBold();
        totalHeaderCell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFE, 0xF3, 0xC7));
        totalHeaderCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        var totalValueCell = worksheet.Cell(1, totalValueCol);
        totalValueCell.FormulaA1 = $"=SUM({amountLetter}:{amountLetter})";
        totalValueCell.Style.Font.SetBold();
        totalValueCell.Style.NumberFormat.Format = AppNumberFormat;
        totalValueCell.Style.Fill.SetBackgroundColor(XLColor.FromArgb(0xFE, 0xF3, 0xC7));
        totalValueCell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);

        worksheet.ShowGridLines = true;

        var remainingLetter = XLHelper.GetColumnLetterFromNumber(colRemaining);
        var displayRowCount = isModeB ? Math.Max(templateRowCount + 1, 200) : templateRowCount + 1;

        if (displayRowCount > 1)
        {
            var tableRange = worksheet.Range(1, 1, displayRowCount, totalValueCol);
            tableRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
            tableRange.Style.Border.SetOutsideBorderColor(borderColor);
            tableRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);
            tableRange.Style.Border.SetInsideBorderColor(borderColor);
            tableRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);

            var dataRange = worksheet.Range(2, 1, displayRowCount, lastColumn);
            dataRange.AddConditionalFormat()
                .WhenIsTrue($"=${remainingLetter}2<0")
                .Fill.SetBackgroundColor(XLColor.FromArgb(0xFE, 0xCD, 0xD3));
        }

        if (!isModeB)
        {
            var hintRow = worksheet.Cell(displayRowCount + 2, 1);
            hintRow.Value = units.Count > 0
                ? "Chỉ nhập số ở các cột đơn vị (ô xanh). Dòng đỏ = xuất quá tồn. Không thêm / sửa mặt hàng — sheet đã khóa."
                : "Chỉ nhập cột SốLượng (ô xanh). Dòng đỏ = xuất quá tồn. Không thêm / sửa mặt hàng — sheet đã khóa.";
            hintRow.Style.Font.SetItalic();
            hintRow.Style.Font.SetFontColor(XLColor.FromArgb(0x64, 0x74, 0x8B));
        }
        else
        {
            // Trong Mode B: đặt ghi chú hướng dẫn ở ô bên phải tổng tiền (dòng 1)
            var hintCell = worksheet.Cell(1, totalValueCol + 1);
            hintCell.Value = "Chế độ B (không giới hạn dòng): Dán/nhập MãHàng & SốLượng vào ô xanh, bấm Ctrl+S để đồng bộ.";
            hintCell.Style.Font.SetItalic();
            hintCell.Style.Font.SetFontColor(XLColor.FromArgb(0x02, 0x84, 0xC7));
        }

        worksheet.Columns().AdjustToContents(5d, 40d);
        worksheet.Column(colStt).Width = 5;
        worksheet.Column(colCategory).Width = Math.Max(5d, worksheet.Column(colCategory).Width * 0.75d);
        worksheet.Column(totalHeaderCol).Width = 10;
        worksheet.Column(totalValueCol).Width = 26;

        if (worksheet.Column(colAmount).Width < 16)
        {
            worksheet.Column(colAmount).Width = 16;
        }

        var protection = worksheet.Protect(SheetPassword);
        protection.AllowElement(XLSheetProtectionElements.FormatColumns);
        protection.AllowElement(XLSheetProtectionElements.FormatRows);
        workbook.SaveAs(filePath);
    }

    /// <summary>Đọc file Excel tạm. Mở với FileShare.ReadWrite để đọc được cả khi Excel đang mở file.</summary>
    public ImportResult<TransactionLineInput> ImportWorkspace(
        string filePath,
        IReadOnlyList<SubordinateUnit> units)
    {
        var result = new ImportResult<TransactionLineInput>();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet(1);

        var headerMap = BuildHeaderMap(worksheet);
        var hasCategory = headerMap.ContainsKey("danhmuc");
        var hasItemCode = headerMap.ContainsKey("mahang");
        if (!hasCategory && !hasItemCode)
        {
            result.Errors.Add(new ImportError { RowNumber = 1, Message = "Thiếu cột DanhMục (hoặc MãHàng ở file cũ)." });
            return result;
        }

        var identityColumn = hasCategory ? headerMap["danhmuc"] : headerMap["mahang"];

        var unitColumns = new List<(int UnitId, int Column)>();
        foreach (var unit in units)
        {
            var byCode = Canonical(unit.UnitCode);
            var byName = Canonical(unit.Name);
            foreach (var pair in headerMap)
            {
                if (ReservedHeaders.Contains(pair.Key))
                {
                    continue;
                }

                if ((byCode.Length > 0 && pair.Key == byCode) || pair.Key == byName)
                {
                    unitColumns.Add((unit.Id, pair.Value));
                    break;
                }
            }
        }

        var hasQuantityColumn = headerMap.TryGetValue("soluong", out var quantityColumn)
            || headerMap.TryGetValue("soluongtong", out quantityColumn);
        if (units.Count > 0 && unitColumns.Count == 0 && !hasQuantityColumn)
        {
            result.Errors.Add(new ImportError
            {
                RowNumber = 1,
                Message = "Không tìm thấy cột đơn vị trực thuộc nào (hoặc cột SốLượng)."
            });
            return result;
        }

        var row = 2;
        var maxRow = worksheet.LastRowUsed()?.RowNumber() ?? 2;
        var consecutiveEmptyRows = 0;

        while (row <= maxRow)
        {
            try
            {
                var identityCell = worksheet.Cell(row, identityColumn);
                var identityText = identityCell.IsEmpty() ? string.Empty : identityCell.GetString().Trim();
                var itemNameText = headerMap.TryGetValue("tenmathang", out var nameColumn) ? worksheet.Cell(row, nameColumn).GetString().Trim() : string.Empty;
                var itemCodeText = hasItemCode ? worksheet.Cell(row, headerMap["mahang"]).GetString().Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(identityText) && string.IsNullOrWhiteSpace(itemNameText) && string.IsNullOrWhiteSpace(itemCodeText))
                {
                    consecutiveEmptyRows++;
                    if (consecutiveEmptyRows >= 50)
                    {
                        break;
                    }
                    row++;
                    continue;
                }

                consecutiveEmptyRows = 0;

                if (identityText.StartsWith("Chỉ nhập", StringComparison.OrdinalIgnoreCase)
                    || identityText.StartsWith("Chế độ B", StringComparison.OrdinalIgnoreCase)
                    || identityText.Equals("Tổng cộng", StringComparison.OrdinalIgnoreCase)
                    || itemNameText.Equals("Tổng cộng", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                var allocations = new Dictionary<int, decimal>();
                foreach (var (unitId, unitColumn) in unitColumns)
                {
                    var value = ReadOptionalDecimal(worksheet, row, unitColumn);
                    if (value is < 0)
                    {
                        throw new InvalidOperationException("Số lượng phân bổ không được âm.");
                    }

                    if (value is > 0)
                    {
                        allocations[unitId] = value.Value;
                    }
                }

                var quantity = unitColumns.Count > 0
                    ? allocations.Values.Sum()
                    : hasQuantityColumn
                        ? ReadOptionalDecimal(worksheet, row, quantityColumn) ?? 0m
                        : 0m;
                if (quantity < 0)
                {
                    throw new InvalidOperationException("SốLượng không được âm.");
                }

                var itemName = headerMap.TryGetValue("tenmathang", out var itemNameCol)
                    ? worksheet.Cell(row, itemNameCol).GetString().Trim()
                    : string.Empty;
                var category = hasCategory
                    ? worksheet.Cell(row, headerMap["danhmuc"]).GetString().Trim()
                    : string.Empty;
                var note = headerMap.TryGetValue("ghichu", out var noteCol)
                    ? worksheet.Cell(row, noteCol).GetString().Trim()
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(itemCodeText) || !string.IsNullOrWhiteSpace(itemName) || quantity > 0)
                {
                    result.Data.Add(new TransactionLineInput
                    {
                        ItemCode = itemCodeText,
                        ItemName = itemName,
                        Category = category,
                        Quantity = quantity,
                        Note = note,
                        Allocations = allocations
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

    private static void ApplyAppNumberFormat(IXLColumn column, bool center = false)
    {
        column.Style.NumberFormat.Format = AppNumberFormat;
        column.Style.Alignment.SetHorizontal(
            center ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Right);
    }

    private static string GetUnitHeader(SubordinateUnit unit) =>
        string.IsNullOrWhiteSpace(unit.UnitCode) ? unit.Name : unit.UnitCode;

    private static decimal? ReadOptionalDecimal(IXLWorksheet worksheet, int row, int column)
    {
        var cell = worksheet.Cell(row, column);
        if (cell.IsEmpty())
        {
            return null;
        }

        try
        {
            if (cell.HasFormula)
            {
                var cached = cell.CachedValue;
                if (cached.IsBlank)
                {
                    return null;
                }

                if (cached.IsNumber)
                {
                    return (decimal)cached.GetNumber();
                }

                var text = cached.ToString()?.Trim();
                return string.IsNullOrWhiteSpace(text) ? null : decimal.Parse(text, CultureInfo.InvariantCulture);
            }

            return string.IsNullOrWhiteSpace(cell.GetString())
                ? null
                : cell.GetValue<decimal>();
        }
        catch
        {
            return null;
        }
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
                map[Canonical(header)] = column;
            }

            column++;
        }

        return map;
    }

    private static string Canonical(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var normalized = header.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark
                || char.IsWhiteSpace(ch)
                || ch is '_' or '-' or '(' or ')')
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
