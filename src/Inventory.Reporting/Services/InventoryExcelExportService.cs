using ClosedXML.Excel;
using Inventory.Domain.Models;

namespace Inventory.Reporting.Services;

public sealed class InventoryExcelExportService
{
    public void ExportReceipt(
        string filePath,
        Receipt receipt,
        IReadOnlyDictionary<int, Item> itemsById)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Phiếu nhập");

        sheet.Cell("A1").Value = $"PHIẾU NHẬP {receipt.ReceiptNumber}";
        sheet.Range("A1:J1").Merge().Style.Font.SetBold().Font.SetFontSize(14);
        WriteLabelValue(sheet, 2, 1, "Ngày", receipt.ReceiptDate);
        WriteLabelValue(sheet, 2, 4, "Theo Kế hoạch", receipt.DeliveredBy);
        WriteLabelValue(sheet, 2, 7, "Theo Hoá đơn số", receipt.DeliveredUnit);
        WriteLabelValue(sheet, 3, 1, "Người nhận", receipt.ReceivedBy);
        WriteLabelValue(sheet, 3, 4, "Lý do nhập", receipt.Reason);

        var headers = new[]
        {
            "STT", "Mã hàng", "Danh mục", "Tên mặt hàng", "ĐVT",
            "Số lượng", "Đơn giá", "Loại sử dụng", "Ghi chú", "Thành tiền"
        };
        WriteHeaders(sheet, 5, headers);

        var rowIndex = 6;
        var number = 1;
        foreach (var line in receipt.Lines.OrderBy(x => x.Id))
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            sheet.Cell(rowIndex, 1).Value = number++;
            sheet.Cell(rowIndex, 2).Value = item?.ItemCode ?? $"#{line.ItemId}";
            sheet.Cell(rowIndex, 3).Value = item?.Category ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = item?.ItemName ?? string.Empty;
            sheet.Cell(rowIndex, 5).Value = item?.Unit ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = line.Quantity;
            sheet.Cell(rowIndex, 7).Value = line.UnitPrice;
            sheet.Cell(rowIndex, 8).Value = DashboardRowViewModelFormatUsage(item?.UsageType);
            sheet.Cell(rowIndex, 9).Value = line.Note ?? string.Empty;
            sheet.Cell(rowIndex, 10).Value = line.Amount;
            rowIndex++;
        }

        var lastDataRow = Math.Max(6, rowIndex - 1);
        sheet.Cell(5, 12).Value = "Tổng tiền phiếu";
        sheet.Cell(5, 12).Style.Font.SetBold();
        sheet.Cell(6, 12).FormulaA1 = lastDataRow >= 6 ? $"=SUM(J6:J{lastDataRow})" : "0";
        sheet.Cell(6, 12).Style.Font.SetBold();
        sheet.Cell(6, 12).Style.NumberFormat.Format = "#,##0";
        sheet.Cell(6, 12).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFFEF3C7"));
        sheet.Cell(6, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        FormatDetailSheet(sheet, 5, lastDataRow, 10, quantityColumns: [6], moneyColumns: [7, 10]);
        var amountRange = sheet.Range(5, 10, lastDataRow, 10);
        amountRange.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF1F5F9"));
        amountRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        amountRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Cell(5, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        sheet.Cell(6, 12).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        sheet.ShowGridLines = true;
        sheet.Cell(2, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    public void ExportBalance(
        string filePath,
        Issue balance,
        IReadOnlyDictionary<int, Item> itemsById,
        IReadOnlyList<SubordinateUnit> subordinateUnits,
        IReadOnlyDictionary<int, BalanceStockSnapshot> stockSnapshots,
        IReadOnlyList<Issue> unitIssues,
        bool omitZeroStockAtIssueDate = false,
        IReadOnlySet<int>? selectedLineIds = null)
    {
        var units = subordinateUnits.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToList();
        var effectiveLines = balance.Lines.OrderBy(x => x.Id).AsEnumerable();
        if (selectedLineIds is not null)
        {
            effectiveLines = effectiveLines.Where(line => selectedLineIds.Contains(line.Id));
        }
        else if (omitZeroStockAtIssueDate)
        {
            effectiveLines = effectiveLines.Where(line =>
                stockSnapshots.TryGetValue(line.ItemId, out var snapshot) && snapshot.TotalQuantity > 0);
        }
        var linesList = effectiveLines.ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Sổ cân đối");

        var headers = new List<string>
        {
            "STT", "Mã hàng", "Danh mục", "Tên mặt hàng", "ĐVT", "Tồn ngày lập phiếu"
        };
        headers.AddRange(units.Select(x =>
            string.IsNullOrWhiteSpace(x.UnitCode) ? x.Name : $"{x.UnitCode} - {x.Name}"));
        headers.AddRange(["Số lượng xuất", "Còn lại", "Đơn giá", "Ghi chú", "Thành tiền"]);

        sheet.Cell(1, 1).Value = $"SỔ CÂN ĐỐI {balance.IssueNumber}";
        sheet.Range(1, 1, 1, headers.Count).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        WriteLabelValue(sheet, 2, 1, "Ngày lập phiếu", balance.IssueDate);
        WriteLabelValue(sheet, 2, 4, "Tổng tiền", linesList.Sum(x => x.Amount));
        WriteHeaders(sheet, 4, headers);

        var rowIndex = 5;
        var number = 1;
        foreach (var line in linesList)
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            stockSnapshots.TryGetValue(line.ItemId, out var snapshot);
            sheet.Cell(rowIndex, 1).Value = number++;
            sheet.Cell(rowIndex, 2).Value = item?.ItemCode ?? $"#{line.ItemId}";
            sheet.Cell(rowIndex, 3).Value = item?.Category ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = item?.ItemName ?? string.Empty;
            sheet.Cell(rowIndex, 5).Value = item?.Unit ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = snapshot?.TotalQuantity ?? 0;

            var column = 7;
            foreach (var unit in units)
            {
                var allocation = line.Allocations.FirstOrDefault(x => x.SubordinateUnitId == unit.Id);
                sheet.Cell(rowIndex, column++).Value = allocation?.Quantity ?? 0;
            }

            sheet.Cell(rowIndex, column++).Value = line.Quantity;
            sheet.Cell(rowIndex, column++).Value = snapshot?.RemainingQuantity ?? 0;
            sheet.Cell(rowIndex, column++).Value = line.UnitPrice;
            sheet.Cell(rowIndex, column++).Value = line.Note ?? string.Empty;
            sheet.Cell(rowIndex, column).Value = line.Amount;
            rowIndex++;
        }

        var unitStart = 7;
        var issuedColumn = unitStart + units.Count;
        var remainingColumn = issuedColumn + 1;
        var priceColumn = remainingColumn + 1;
        var noteColumn = priceColumn + 1;
        var amountColumn = noteColumn + 1;
        var quantityColumns = Enumerable.Range(unitStart, units.Count)
            .Concat([6, issuedColumn, remainingColumn])
            .ToArray();
        FormatDetailSheet(
            sheet,
            4,
            rowIndex - 1,
            headers.Count,
            quantityColumns,
            [priceColumn, amountColumn]);
        sheet.Cell(2, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        sheet.SheetView.FreezeRows(4);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.Columns().AdjustToContents();

        foreach (var unitIssue in unitIssues
                     .OrderBy(x => x.ReceivedUnit)
                     .ThenBy(x => x.IssueNumber))
        {
            WriteUnitIssueSheet(workbook, unitIssue, itemsById);
        }

        workbook.SaveAs(filePath);
    }

    private static void WriteUnitIssueSheet(
        XLWorkbook workbook,
        Issue issue,
        IReadOnlyDictionary<int, Item> itemsById)
    {
        var unitLabel = string.IsNullOrWhiteSpace(issue.ReceivedUnitCode)
            ? issue.ReceivedUnit
            : issue.ReceivedUnitCode;
        var sheetName = CreateUniqueWorksheetName(workbook, $"PX {unitLabel}");
        var sheet = workbook.AddWorksheet(sheetName);
        var headers = new[]
        {
            "STT", "Mã hàng", "Danh mục", "Tên mặt hàng", "ĐVT",
            "Số lượng", "Đơn giá", "Ghi chú", "Thành tiền"
        };

        sheet.Cell(1, 1).Value = $"PHIẾU XUẤT {issue.IssueNumber}";
        sheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        WriteLabelValue(sheet, 2, 1, "Ngày phiếu", issue.IssueDate);
        WriteLabelValue(sheet, 2, 4, "Đơn vị nhận", issue.ReceivedUnit);
        WriteLabelValue(sheet, 3, 1, "Người nhận", issue.ReceivedBy);
        WriteLabelValue(sheet, 3, 4, "Lý do xuất", issue.Reason);
        WriteHeaders(sheet, 5, headers);

        var rowIndex = 6;
        foreach (var line in issue.Lines.OrderBy(x => x.Id))
        {
            itemsById.TryGetValue(line.ItemId, out var item);
            sheet.Cell(rowIndex, 1).Value = rowIndex - 5;
            sheet.Cell(rowIndex, 2).Value = item?.ItemCode ?? $"#{line.ItemId}";
            sheet.Cell(rowIndex, 3).Value = item?.Category ?? string.Empty;
            sheet.Cell(rowIndex, 4).Value = item?.ItemName ?? string.Empty;
            sheet.Cell(rowIndex, 5).Value = item?.Unit ?? string.Empty;
            sheet.Cell(rowIndex, 6).Value = line.Quantity;
            sheet.Cell(rowIndex, 7).Value = line.UnitPrice;
            sheet.Cell(rowIndex, 8).Value = line.Note ?? string.Empty;
            sheet.Cell(rowIndex, 9).Value = line.Amount;
            rowIndex++;
        }

        FormatDetailSheet(sheet, 5, rowIndex - 1, headers.Length, [6], [7, 9]);
        sheet.Cell(2, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        sheet.Columns().AdjustToContents();
    }

    private static string CreateUniqueWorksheetName(XLWorkbook workbook, string requestedName)
    {
        var invalid = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var cleaned = string.Concat(requestedName.Select(x => invalid.Contains(x) ? '_' : x)).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "Phiếu xuất";
        }

        var baseName = cleaned[..Math.Min(cleaned.Length, 31)];
        var candidate = baseName;
        var suffix = 2;
        while (workbook.Worksheets.Any(x => string.Equals(x.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            var suffixText = $" ({suffix++})";
            candidate = baseName[..Math.Min(baseName.Length, 31 - suffixText.Length)] + suffixText;
        }

        return candidate;
    }

    public void ExportUnitIssueReport(
        string filePath,
        IReadOnlyList<UnitIssueReportRow> rows,
        IReadOnlyList<UnitIssueReportLine> lines,
        DateTime fromDate,
        DateTime toDate)
    {
        using var workbook = new XLWorkbook();
        WriteUnitIssueSummarySheet(workbook, rows, fromDate, toDate);
        WriteUnitIssueGroupSheet(
            workbook,
            "Theo đơn vị",
            rows.GroupBy(x => new { x.SubordinateUnitId, x.UnitCode, x.UnitName })
                .OrderBy(x => x.Key.UnitName)
                .Select(x => new ReportGroup(
                    string.IsNullOrWhiteSpace(x.Key.UnitCode)
                        ? x.Key.UnitName
                        : $"{x.Key.UnitCode} - {x.Key.UnitName}",
                    x.ToList())));
        WriteUnitIssueGroupSheet(
            workbook,
            "Theo tháng",
            rows.GroupBy(x => new { x.IssueDate.Year, x.IssueDate.Month })
                .OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month)
                .Select(x => new ReportGroup(
                    $"Tháng {x.Key.Month:00}/{x.Key.Year}",
                    x.ToList())));
        WriteUnitIssueLinesSheet(workbook, lines);
        workbook.SaveAs(filePath);
    }

    private static void WriteUnitIssueSummarySheet(
        XLWorkbook workbook,
        IReadOnlyList<UnitIssueReportRow> rows,
        DateTime fromDate,
        DateTime toDate)
    {
        var sheet = workbook.AddWorksheet("Chi tiết phiếu");
        var headers = new[]
        {
            "STT", "Ngày phiếu", "Số phiếu", "Mã đơn vị", "Tên đơn vị",
            "Người nhận", "Lý do xuất", "Số khoản", "Tổng số lượng", "Tổng tiền"
        };
        sheet.Cell(1, 1).Value = $"BÁO CÁO PHIẾU XUẤT ĐƠN VỊ ({fromDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy})";
        sheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.SetBold().Font.SetFontSize(14);
        WriteHeaders(sheet, 3, headers);
        var rowIndex = 4;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = rowIndex - 3;
            sheet.Cell(rowIndex, 2).Value = row.IssueDate;
            sheet.Cell(rowIndex, 3).Value = row.IssueNumber;
            sheet.Cell(rowIndex, 4).Value = row.UnitCode;
            sheet.Cell(rowIndex, 5).Value = row.UnitName;
            sheet.Cell(rowIndex, 6).Value = row.ReceivedBy;
            sheet.Cell(rowIndex, 7).Value = row.Reason;
            sheet.Cell(rowIndex, 8).Value = row.LineCount;
            sheet.Cell(rowIndex, 9).Value = row.TotalQuantity;
            sheet.Cell(rowIndex, 10).Value = row.TotalAmount;
            rowIndex++;
        }

        FormatDetailSheet(sheet, 3, rowIndex - 1, headers.Length, [9], [10]);
        if (rowIndex > 4)
        {
            sheet.Range(4, 2, rowIndex - 1, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        }
        AdjustReportColumns(sheet);
    }

    private static void WriteUnitIssueGroupSheet(
        XLWorkbook workbook,
        string sheetName,
        IEnumerable<ReportGroup> groups)
    {
        var sheet = workbook.AddWorksheet(sheetName);
        var headers = new[] { "STT", "Nhóm báo cáo", "Số phiếu", "Số khoản", "Tổng số lượng", "Tổng tiền" };
        WriteHeaders(sheet, 1, headers);
        var rowIndex = 2;
        foreach (var group in groups)
        {
            sheet.Cell(rowIndex, 1).Value = rowIndex - 1;
            sheet.Cell(rowIndex, 2).Value = group.Label;
            sheet.Cell(rowIndex, 3).Value = group.Rows.Count;
            sheet.Cell(rowIndex, 4).Value = group.Rows.Sum(x => x.LineCount);
            sheet.Cell(rowIndex, 5).Value = group.Rows.Sum(x => x.TotalQuantity);
            sheet.Cell(rowIndex, 6).Value = group.Rows.Sum(x => x.TotalAmount);
            rowIndex++;
        }

        FormatDetailSheet(sheet, 1, rowIndex - 1, headers.Length, [5], [6]);
        AdjustReportColumns(sheet);
    }

    private static void WriteUnitIssueLinesSheet(
        XLWorkbook workbook,
        IReadOnlyList<UnitIssueReportLine> lines)
    {
        var sheet = workbook.AddWorksheet("Chi tiết hàng hóa");
        var headers = new[]
        {
            "STT", "Ngày phiếu", "Số phiếu", "Mã đơn vị", "Tên đơn vị",
            "Mã hàng", "Danh mục", "Tên mặt hàng", "ĐVT", "Số lượng",
            "Đơn giá", "Ghi chú", "Thành tiền"
        };
        WriteHeaders(sheet, 1, headers);
        var rowIndex = 2;
        foreach (var line in lines)
        {
            sheet.Cell(rowIndex, 1).Value = rowIndex - 1;
            sheet.Cell(rowIndex, 2).Value = line.IssueDate;
            sheet.Cell(rowIndex, 3).Value = line.IssueNumber;
            sheet.Cell(rowIndex, 4).Value = line.UnitCode;
            sheet.Cell(rowIndex, 5).Value = line.UnitName;
            sheet.Cell(rowIndex, 6).Value = line.ItemCode;
            sheet.Cell(rowIndex, 7).Value = line.Category;
            sheet.Cell(rowIndex, 8).Value = line.ItemName;
            sheet.Cell(rowIndex, 9).Value = line.Unit;
            sheet.Cell(rowIndex, 10).Value = line.Quantity;
            sheet.Cell(rowIndex, 11).Value = line.UnitPrice;
            sheet.Cell(rowIndex, 12).Value = line.Note ?? string.Empty;
            sheet.Cell(rowIndex, 13).Value = line.Amount;
            rowIndex++;
        }

        FormatDetailSheet(sheet, 1, rowIndex - 1, headers.Length, [10], [11, 13]);
        if (rowIndex > 2)
        {
            sheet.Range(2, 2, rowIndex - 1, 2).Style.DateFormat.Format = "dd/MM/yyyy";
        }
        AdjustReportColumns(sheet);
    }

    private static void AdjustReportColumns(IXLWorksheet sheet)
    {
        sheet.Columns().AdjustToContents(1, 80);
        foreach (var column in sheet.ColumnsUsed())
        {
            if (column.Width > 35)
            {
                column.Width = 35;
            }
        }
    }

    private sealed record ReportGroup(string Label, IReadOnlyList<UnitIssueReportRow> Rows);

    private static void WriteLabelValue(IXLWorksheet sheet, int row, int column, string label, object value)
    {
        sheet.Cell(row, column).Value = label;
        sheet.Cell(row, column).Style.Font.SetBold();
        sheet.Cell(row, column + 1).Value = XLCellValue.FromObject(value);
    }

    private static void WriteHeaders(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            sheet.Cell(row, i + 1).Value = headers[i];
        }

        var range = sheet.Range(row, 1, row, headers.Count);
        range.Style.Font.SetBold();
        range.Style.Fill.BackgroundColor = XLColor.LightBlue;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void FormatDetailSheet(
        IXLWorksheet sheet,
        int headerRow,
        int lastRow,
        int columnCount,
        IReadOnlyCollection<int> quantityColumns,
        IReadOnlyCollection<int> moneyColumns)
    {
        if (lastRow >= headerRow)
        {
            var range = sheet.Range(headerRow, 1, lastRow, columnCount);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            range.SetAutoFilter();
        }

        if (lastRow > headerRow)
        {
            foreach (var column in quantityColumns)
            {
                sheet.Range(headerRow + 1, column, lastRow, column).Style.NumberFormat.Format = "#,##0";
            }

            foreach (var column in moneyColumns)
            {
                sheet.Range(headerRow + 1, column, lastRow, column).Style.NumberFormat.Format = "#,##0";
            }
        }

        sheet.SheetView.FreezeRows(headerRow);
    }

    private static string DashboardRowViewModelFormatUsage(string? usageType) =>
        string.IsNullOrWhiteSpace(usageType)
            ? "Tự mua sắm"
            : usageType.Trim().Equals("trên cấp", StringComparison.OrdinalIgnoreCase)
                || usageType.Trim().Equals("trên cap", StringComparison.OrdinalIgnoreCase)
                    ? "Trên cấp"
                    : "Tự mua sắm";

    // -------------------------------------------------------------------------
    // Tổng hợp xuất theo đơn vị
    // -------------------------------------------------------------------------

    /// <summary>
    /// <summary>
    /// Xuất tổng hợp các mặt hàng đã xuất cho từng đơn vị trong khoảng thời gian.
    /// Sheet 1: Sheet tổng hợp chung (mỗi đơn vị 1 cột).
    /// Sheet 2..N: Sheet chi tiết từng đơn vị.
    /// units: (UnitId, UnitCode, UnitName, Label) — Label là tên hiển thị trên cột/sheet.
    /// </summary>
    public void ExportUnitSummary(
        string filePath,
        IReadOnlyList<UnitIssueReportLine> lines,
        IReadOnlyList<(int UnitId, string UnitCode, string UnitName, string Label)> units,
        DateTime fromDate,
        DateTime toDate)
    {
        using var workbook = new XLWorkbook();
        var period = $"{fromDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy}";

        // Sheet 1: Sheet tổng hợp
        WriteUnitSummaryCombinedSheet(workbook, lines, units, period);

        // Các sheet tiếp theo: Chi tiết từng đơn vị
        WriteUnitSummarySeparateSheets(workbook, lines, units, period);

        workbook.SaveAs(filePath);
    }

    private static void WriteUnitSummaryCombinedSheet(
        XLWorkbook workbook,
        IReadOnlyList<UnitIssueReportLine> lines,
        IReadOnlyList<(int UnitId, string UnitCode, string UnitName, string Label)> units,
        string period)
    {
        var sheet = workbook.AddWorksheet("Tổng hợp");

        // Build unique item list sorted by category then name
        var itemKeys = lines
            .GroupBy(l => l.ItemId)
            .Select(g =>
            {
                var first = g.First();
                var notes = string.Join("; ", g.Select(x => x.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                return new ItemSummaryKey(
                    first.ItemId,
                    first.ItemCode,
                    first.Category,
                    first.ItemName,
                    first.Unit,
                    first.UnitPrice,
                    DashboardRowViewModelFormatUsage(first.UsageType),
                    notes);
            })
            .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Build lookup: (unitId, itemId) → total quantity
        var quantityLookup = lines.ToLookup(l => (unitId: FindUnitId(l, units), itemId: l.ItemId));

        // Title & headers
        var title = $"TỔNG HỢP HÀNG XUẤT THEO ĐƠN VỊ ({period})";
        var totalColumns = 8 + units.Count + 1; // STT, Mã, Danh mục, Tên, ĐVT, Đơn giá, Loại hàng, Ghi chú, [units…], Tổng cộng
        sheet.Cell(1, 1).Value = title;
        sheet.Range(1, 1, 1, totalColumns).Merge().Style.Font.SetBold().Font.SetFontSize(13);

        const int headerRow = 3;
        sheet.Cell(headerRow, 1).Value = "STT";
        sheet.Cell(headerRow, 2).Value = "Mã hàng";
        sheet.Cell(headerRow, 3).Value = "Danh mục";
        sheet.Cell(headerRow, 4).Value = "Tên mặt hàng";
        sheet.Cell(headerRow, 5).Value = "ĐVT";
        sheet.Cell(headerRow, 6).Value = "Đơn giá";
        sheet.Cell(headerRow, 7).Value = "Loại hàng";
        sheet.Cell(headerRow, 8).Value = "Ghi chú";
        for (var u = 0; u < units.Count; u++)
        {
            sheet.Cell(headerRow, 9 + u).Value = units[u].Label;
        }
        sheet.Cell(headerRow, 9 + units.Count).Value = "Tổng cộng";

        var headerRange = sheet.Range(headerRow, 1, headerRow, totalColumns);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.WrapText = true;

        const int dataStartRow = headerRow + 1;
        var rowIndex = dataStartRow;
        var stt = 1;

        foreach (var item in itemKeys)
        {
            sheet.Cell(rowIndex, 1).Value = stt++;
            sheet.Cell(rowIndex, 2).Value = item.ItemCode;
            sheet.Cell(rowIndex, 3).Value = item.Category;
            sheet.Cell(rowIndex, 4).Value = item.ItemName;
            sheet.Cell(rowIndex, 5).Value = item.Unit;
            sheet.Cell(rowIndex, 6).Value = item.UnitPrice;
            sheet.Cell(rowIndex, 7).Value = item.UsageType;
            sheet.Cell(rowIndex, 8).Value = item.Note;

            decimal rowTotal = 0;
            for (var u = 0; u < units.Count; u++)
            {
                var unitId = units[u].UnitId;
                var qty = quantityLookup[(unitId, item.ItemId)].Sum(l => l.Quantity);
                if (qty > 0)
                {
                    sheet.Cell(rowIndex, 9 + u).Value = qty;
                    rowTotal += qty;
                }
            }

            if (rowTotal > 0)
            {
                sheet.Cell(rowIndex, 9 + units.Count).Value = rowTotal;
            }

            rowIndex++;
        }

        // Totals row
        var lastDataRow = rowIndex - 1;
        if (lastDataRow >= dataStartRow)
        {
            var totalRow = rowIndex;
            sheet.Cell(totalRow, 4).Value = "TỔNG CỘNG";
            sheet.Cell(totalRow, 4).Style.Font.SetBold();

            for (var u = 0; u < units.Count; u++)
            {
                var col = 9 + u;
                sheet.Cell(totalRow, col).FormulaA1 =
                    $"=SUM({sheet.Cell(dataStartRow, col).Address}:{sheet.Cell(lastDataRow, col).Address})";
                sheet.Cell(totalRow, col).Style.Font.SetBold();
                sheet.Cell(totalRow, col).Style.NumberFormat.Format = "#,##0";
            }

            var grandCol = 9 + units.Count;
            sheet.Cell(totalRow, grandCol).FormulaA1 =
                $"=SUM({sheet.Cell(dataStartRow, grandCol).Address}:{sheet.Cell(lastDataRow, grandCol).Address})";
            sheet.Cell(totalRow, grandCol).Style.Font.SetBold();
            sheet.Cell(totalRow, grandCol).Style.NumberFormat.Format = "#,##0";
            sheet.Range(totalRow, 1, totalRow, totalColumns)
                .Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF1F5F9");

            // Format data area (header + data + totals)
            var fullRange = sheet.Range(headerRow, 1, totalRow, totalColumns);
            fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

            // UnitPrice column format
            sheet.Range(dataStartRow, 6, lastDataRow, 6)
                .Style.NumberFormat.Format = "#,##0";
            sheet.Range(dataStartRow, 6, lastDataRow, 6)
                .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            // Number format on quantity columns
            for (var u = 0; u <= units.Count; u++)
            {
                var col = 9 + u;
                sheet.Range(dataStartRow, col, lastDataRow, col)
                    .Style.NumberFormat.Format = "#,##0";
                sheet.Range(dataStartRow, col, lastDataRow, col)
                    .Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }
        else
        {
            // No data rows — still format header
            sheet.Range(headerRow, 1, headerRow, totalColumns)
                .Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Column(4).Width = 35;
        sheet.Columns(1, 3).AdjustToContents();
        sheet.Columns(5, totalColumns).AdjustToContents();
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.FitToPages(1, 0);
    }

    private static void WriteUnitSummarySeparateSheets(
        XLWorkbook workbook,
        IReadOnlyList<UnitIssueReportLine> lines,
        IReadOnlyList<(int UnitId, string UnitCode, string UnitName, string Label)> units,
        string period)
    {
        // Item master list across all units
        var allItemKeys = lines
            .GroupBy(l => l.ItemId)
            .Select(g =>
            {
                var first = g.First();
                var notes = string.Join("; ", g.Select(x => x.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());
                return new ItemSummaryKey(
                    first.ItemId,
                    first.ItemCode,
                    first.Category,
                    first.ItemName,
                    first.Unit,
                    first.UnitPrice,
                    DashboardRowViewModelFormatUsage(first.UsageType),
                    notes);
            })
            .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var linesByUnit = lines.ToLookup(l => FindUnitId(l, units));

        foreach (var (unitId, unitCode, unitName, label) in units)
        {
            var sheetName = CreateUniqueWorksheetName(workbook, label);
            var sheet = workbook.AddWorksheet(sheetName);

            var quantityByItem = linesByUnit[unitId]
                .GroupBy(l => l.ItemId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

            var title = $"HÀNG XUẤT ĐƠN VỊ: {label} ({period})";
            sheet.Cell(1, 1).Value = title;
            sheet.Range(1, 1, 1, 10).Merge().Style.Font.SetBold().Font.SetFontSize(13);

            var headers = new[] { "STT", "Mã hàng", "Danh mục", "Tên mặt hàng", "ĐVT", "Đơn giá", "Loại hàng", "Ghi chú", "Số lượng", "Thành tiền" };
            WriteHeaders(sheet, 3, headers);

            var rowIndex = 4;
            var stt = 1;
            foreach (var item in allItemKeys)
            {
                if (!quantityByItem.TryGetValue(item.ItemId, out var qty) || qty <= 0)
                {
                    continue;
                }

                sheet.Cell(rowIndex, 1).Value = stt++;
                sheet.Cell(rowIndex, 2).Value = item.ItemCode;
                sheet.Cell(rowIndex, 3).Value = item.Category;
                sheet.Cell(rowIndex, 4).Value = item.ItemName;
                sheet.Cell(rowIndex, 5).Value = item.Unit;
                sheet.Cell(rowIndex, 6).Value = item.UnitPrice;
                sheet.Cell(rowIndex, 7).Value = item.UsageType;
                sheet.Cell(rowIndex, 8).Value = item.Note;
                sheet.Cell(rowIndex, 9).Value = qty;
                sheet.Cell(rowIndex, 10).FormulaA1 = $"=F{rowIndex}*I{rowIndex}";
                rowIndex++;
            }

            var lastRow = rowIndex - 1;
            if (lastRow >= 4)
            {
                var dataRange = sheet.Range(3, 1, lastRow, 10);
                dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                sheet.Range(4, 6, lastRow, 6).Style.NumberFormat.Format = "#,##0";
                sheet.Range(4, 9, lastRow, 9).Style.NumberFormat.Format = "#,##0";
                sheet.Range(4, 10, lastRow, 10).Style.NumberFormat.Format = "#,##0";

                var totalRow = rowIndex;
                sheet.Cell(totalRow, 4).Value = "TỔNG CỘNG";
                sheet.Cell(totalRow, 4).Style.Font.SetBold();
                sheet.Cell(totalRow, 10).FormulaA1 = $"=SUM(J4:J{lastRow})";
                sheet.Cell(totalRow, 10).Style.Font.SetBold();
                sheet.Cell(totalRow, 10).Style.NumberFormat.Format = "#,##0";
                sheet.Range(totalRow, 1, totalRow, 10)
                    .Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF1F5F9");
            }
            else
            {
                sheet.Cell(4, 1).Value = "(Không có hàng xuất trong kỳ)";
            }

            sheet.SheetView.FreezeRows(3);
            sheet.Column(4).Width = 35;
            sheet.Columns(1, 3).AdjustToContents();
            sheet.Columns(5, 10).AdjustToContents();
        }
    }

    /// <summary>
    /// Khớp một dòng xuất với đơn vị trong danh sách.
    /// Ưu tiên: UnitCode (nếu cả hai không rỗng) → UnitName.
    /// </summary>
    private static int FindUnitId(
        UnitIssueReportLine line,
        IReadOnlyList<(int UnitId, string UnitCode, string UnitName, string Label)> units)
    {
        foreach (var (unitId, unitCode, unitName, _) in units)
        {
            // Khớp theo mã trước (chính xác nhất)
            if (!string.IsNullOrWhiteSpace(line.UnitCode)
                && !string.IsNullOrWhiteSpace(unitCode)
                && string.Equals(line.UnitCode, unitCode, StringComparison.OrdinalIgnoreCase))
            {
                return unitId;
            }
        }

        // Fallback: khớp theo tên
        foreach (var (unitId, _, unitName, _) in units)
        {
            if (string.Equals(line.UnitName, unitName, StringComparison.OrdinalIgnoreCase))
            {
                return unitId;
            }
        }

        return -1;
    }

    private sealed record ItemSummaryKey(
        int ItemId,
        string ItemCode,
        string Category,
        string ItemName,
        string Unit,
        decimal UnitPrice,
        string UsageType,
        string Note);
}

