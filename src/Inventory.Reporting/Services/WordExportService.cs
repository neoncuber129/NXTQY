using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Inventory.Domain.Models;
using Inventory.Domain.Services;
using System.Globalization;

namespace Inventory.Reporting.Services;

public sealed class WordExportService
{
    private static readonly CultureInfo VietnameseCulture = CultureInfo.GetCultureInfo("vi-VN");

    public void ExportReceipt(
        string templatePath,
        string outputPath,
        Receipt receipt,
        IReadOnlyDictionary<int, Item>? itemsById = null)
    {
        File.Copy(templatePath, outputPath, overwrite: true);
        var amountInWords = ResolveAmountInWords(receipt.TotalAmount);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${SoPhieu}"] = receipt.ReceiptNumber,
            ["${Ngay}"] = receipt.ReceiptDate.ToString("dd/MM/yyyy"),
            ["${ngaychu}"] = FormatDateInWords(receipt.ReceiptDate),
            ["${TheoKeHoach}"] = receipt.DeliveredBy,
            ["${TheoHoaDonSo}"] = receipt.DeliveredUnit,
            // Tương thích các template phiếu nhập cũ.
            ["${NguoiGiao}"] = receipt.DeliveredBy,
            ["${DonViNguoiGiao}"] = receipt.DeliveredUnit,
            ["${NguoiNhan}"] = receipt.ReceivedBy,
            ["${LyDoNhap}"] = receipt.Reason,
            ["${TongTien}"] = FormatNumber(receipt.TotalAmount),
            ["${TongTienBangChu}"] = amountInWords,
            ["${SoTienBangChu}"] = amountInWords,
            ["${SoKhoan}"] = receipt.Lines.Count.ToString(),
            ["${sokhoanchu}"] = FormatCountInWords(receipt.Lines.Count)
        };

        var detailRows = BuildReceiptDetailRows(receipt, itemsById);
        RenderTemplate(outputPath, tokens, detailRows);
    }

    public void ExportIssue(
        string templatePath,
        string outputPath,
        Issue issue,
        IReadOnlyDictionary<int, Item>? itemsById = null)
    {
        File.Copy(templatePath, outputPath, overwrite: true);
        var amountInWords = ResolveAmountInWords(issue.TotalAmount);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${SoPhieu}"] = issue.IssueNumber,
            ["${Ngay}"] = issue.IssueDate.ToString("dd/MM/yyyy"),
            ["${ngaychu}"] = FormatDateInWords(issue.IssueDate),
            ["${NguoiNhan}"] = issue.ReceivedBy,
            ["${DonViNguoiNhan}"] = issue.ReceivedUnit,
            ["${MaDonVi}"] = issue.ReceivedUnitCode,
            ["${NguoiGiao}"] = issue.DeliveredBy,
            ["${LyDoXuat}"] = issue.Reason,
            ["${TongTien}"] = FormatNumber(issue.TotalAmount),
            ["${TongTienBangChu}"] = amountInWords,
            ["${SoTienBangChu}"] = amountInWords,
            ["${SoKhoan}"] = issue.Lines.Count.ToString(),
            ["${sokhoanchu}"] = FormatCountInWords(issue.Lines.Count)
        };

        var detailRows = BuildIssueDetailRows(issue, itemsById);
        RenderTemplate(outputPath, tokens, detailRows);
    }

    /// <summary>
    /// Xuất nhiều phiếu xuất vào cùng một file Word (mỗi phiếu một trang, ngắt trang giữa các phiếu).
    /// </summary>
    public void ExportIssuesCombined(
        string templatePath,
        string outputPath,
        IReadOnlyList<Issue> issues,
        IReadOnlyDictionary<int, Item>? itemsById = null)
    {
        if (issues.Count == 0)
        {
            throw new InvalidOperationException("Không có phiếu xuất để xuất Word.");
        }

        if (issues.Count == 1)
        {
            ExportIssue(templatePath, outputPath, issues[0], itemsById);
            return;
        }

        ExportIssue(templatePath, outputPath, issues[0], itemsById);

        var tempFiles = new List<string>();
        try
        {
            for (var i = 1; i < issues.Count; i++)
            {
                var temp = Path.Combine(Path.GetTempPath(), $"px_merge_{Guid.NewGuid():N}.docx");
                ExportIssue(templatePath, temp, issues[i], itemsById);
                tempFiles.Add(temp);
            }

            using var destination = WordprocessingDocument.Open(outputPath, true);
            var destPart = destination.MainDocumentPart
                ?? throw new InvalidOperationException("Không đọc được nội dung file Word.");
            var destBody = destPart.Document?.Body
                ?? throw new InvalidOperationException("Không đọc được nội dung file Word.");

            var sectionProps = destBody.Elements<SectionProperties>().LastOrDefault();
            sectionProps?.Remove();

            foreach (var temp in tempFiles)
            {
                using var source = WordprocessingDocument.Open(temp, false);
                var srcBody = source.MainDocumentPart?.Document?.Body
                    ?? throw new InvalidOperationException("Không đọc được nội dung phiếu xuất tạm.");

                destBody.AppendChild(new Paragraph(
                    new Run(new Break { Type = BreakValues.Page })));

                foreach (var element in srcBody.Elements())
                {
                    if (element is SectionProperties)
                    {
                        continue;
                    }

                    destBody.AppendChild(element.CloneNode(true));
                }
            }

            if (sectionProps is not null)
            {
                destBody.AppendChild(sectionProps.CloneNode(true));
            }

            destPart.Document!.Save();
        }
        finally
        {
            foreach (var temp in tempFiles)
            {
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                    // ignore temp cleanup failures
                }
            }
        }
    }

    /// <summary>
    /// Xuất sổ cân đối: cột số lượng theo từng đơn vị trực thuộc + tổng.
    /// Dùng template để lấy phần đầu phiếu; bảng chi tiết được dựng lại theo danh sách đơn vị.
    /// </summary>
    public void ExportBalanceOverview(
        string templatePath,
        string outputPath,
        Issue balance,
        IReadOnlyDictionary<int, Item>? itemsById,
        IReadOnlyList<SubordinateUnit> subordinateUnits,
        IReadOnlyDictionary<int, BalanceStockSnapshot>? stockSnapshots = null)
    {
        var units = subordinateUnits
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToList();

        File.Copy(templatePath, outputPath, overwrite: true);
        var amountInWords = ResolveAmountInWords(balance.TotalAmount);
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${SoPhieu}"] = balance.IssueNumber,
            ["${Ngay}"] = balance.IssueDate.ToString("dd/MM/yyyy"),
            ["${ngaychu}"] = FormatDateInWords(balance.IssueDate),
            ["${NguoiNhan}"] = balance.ReceivedBy,
            ["${DonViNguoiNhan}"] = "Sổ cân đối các đơn vị",
            ["${NguoiGiao}"] = balance.DeliveredBy,
            ["${LyDoXuat}"] = string.IsNullOrWhiteSpace(balance.Reason) ? "Sổ cân đối" : balance.Reason,
            ["${TongTien}"] = FormatNumber(balance.TotalAmount),
            ["${TongTienBangChu}"] = amountInWords,
            ["${SoTienBangChu}"] = amountInWords,
            ["${SoKhoan}"] = balance.Lines.Count.ToString(),
            ["${sokhoanchu}"] = FormatCountInWords(balance.Lines.Count)
        };

        using var document = WordprocessingDocument.Open(outputPath, true);
        var mainPart = document.MainDocumentPart;
        if (mainPart?.Document?.Body is not Body body)
        {
            throw new InvalidOperationException("Không đọc được nội dung file Word.");
        }

        // Cặp placeholder số 1 là cột mẫu. Nhân bản cột sang phải cho các
        // đơn vị còn lại, giữ nguyên vị trí và định dạng do người dùng thiết kế.
        ExpandUnitPlaceholderColumns(body, units.Count);
        var bodyText = string.Concat(body.Descendants<Text>().Select(x => x.Text));
        var hasDynamicBalancePlaceholders =
            bodyText.Contains("${SL1}", StringComparison.OrdinalIgnoreCase)
            || bodyText.Contains("${SoLuongDonVi1}", StringComparison.OrdinalIgnoreCase)
            || bodyText.Contains("${SLTong}", StringComparison.OrdinalIgnoreCase)
            || bodyText.Contains("${SLConlai}", StringComparison.OrdinalIgnoreCase)
            || bodyText.Contains("${SoLuongTong}", StringComparison.OrdinalIgnoreCase)
            || bodyText.Contains("${SoLuongConLai}", StringComparison.OrdinalIgnoreCase);
        if (hasDynamicBalancePlaceholders)
        {
            for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                tokens[$"${{DonVi{unitIndex + 1}}}"] = units[unitIndex].Name;
                tokens[$"${{Madv{unitIndex + 1}}}"] = units[unitIndex].UnitCode;
                tokens[$"${{MaDonVi{unitIndex + 1}}}"] = units[unitIndex].UnitCode;
            }

            var detailRows = BuildBalanceOverviewDetailRows(
                balance,
                itemsById,
                units,
                stockSnapshots);
            RenderDynamicTableRows(body, detailRows);
            ReplaceTokensInDocumentParts(mainPart, body, tokens);
            mainPart.Document!.Save();
            return;
        }

        // Tương thích template cũ chưa có placeholder đơn vị.
        ReplaceDetailTableWithBalanceOverview(body, balance, itemsById, units);
        ReplaceTokensInDocumentParts(mainPart, body, tokens);
        mainPart.Document!.Save();
    }

    private static void ExpandUnitPlaceholderColumns(Body body, int unitCount)
    {
        if (unitCount <= 1)
        {
            return;
        }

        var headerCell = body.Descendants<TableCell>()
            .FirstOrDefault(cell =>
            {
                var text = GetElementText(cell);
                return text.Contains("${DonVi1}", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("${Madv1}", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("${MaDonVi1}", StringComparison.OrdinalIgnoreCase);
            });
        var detailCell = body.Descendants<TableCell>()
            .FirstOrDefault(cell =>
            {
                var text = GetElementText(cell);
                return text.Contains("${SL1}", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("${SoLuongDonVi1}", StringComparison.OrdinalIgnoreCase);
            });
        if (headerCell?.Parent is not TableRow headerRow
            || detailCell?.Parent is not TableRow detailRow
            || headerRow.Parent is not Table table
            || !ReferenceEquals(detailRow.Parent, table))
        {
            return;
        }

        var modelColumnIndex = GetGridColumnIndex(headerRow, headerCell);
        OpenXmlElement previousHeader = headerCell;
        OpenXmlElement previousDetail = detailCell;
        for (var index = 2; index <= unitCount; index++)
        {
            var clonedHeader = (TableCell)headerCell.CloneNode(true);
            ReplaceTokensInContainer(
                clonedHeader,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["${DonVi1}"] = $"${{DonVi{index}}}",
                    ["${Madv1}"] = $"${{Madv{index}}}",
                    ["${MaDonVi1}"] = $"${{MaDonVi{index}}}"
                });
            previousHeader.InsertAfterSelf(clonedHeader);
            previousHeader = clonedHeader;

            var clonedDetail = (TableCell)detailCell.CloneNode(true);
            ReplaceTokensInContainer(
                clonedDetail,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["${SL1}"] = $"${{SL{index}}}",
                    ["${SoLuongDonVi1}"] = $"${{SoLuongDonVi{index}}}"
                });
            previousDetail.InsertAfterSelf(clonedDetail);
            previousDetail = clonedDetail;
        }

        ExpandRemainingTableRows(
            table,
            headerRow,
            detailRow,
            modelColumnIndex,
            unitCount - 1);
    }

    private static int GetGridColumnIndex(TableRow row, TableCell targetCell)
    {
        var index = 0;
        foreach (var cell in row.Elements<TableCell>())
        {
            if (ReferenceEquals(cell, targetCell))
            {
                return index;
            }

            index += GetGridSpan(cell);
        }

        return index;
    }

    private static int GetGridSpan(TableCell cell) =>
        cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;

    private static void ExpandRemainingTableRows(
        Table table,
        TableRow headerRow,
        TableRow detailRow,
        int modelColumnIndex,
        int additionalColumnCount)
    {
        if (additionalColumnCount <= 0)
        {
            return;
        }

        foreach (var row in table.Elements<TableRow>()
                     .Where(x => !ReferenceEquals(x, headerRow) && !ReferenceEquals(x, detailRow)))
        {
            var gridIndex = 0;
            foreach (var cell in row.Elements<TableCell>().ToList())
            {
                var span = GetGridSpan(cell);
                if (modelColumnIndex < gridIndex || modelColumnIndex >= gridIndex + span)
                {
                    gridIndex += span;
                    continue;
                }

                if (span > 1)
                {
                    var properties = cell.GetFirstChild<TableCellProperties>()
                        ?? cell.PrependChild(new TableCellProperties());
                    var gridSpan = properties.GetFirstChild<GridSpan>()
                        ?? properties.AppendChild(new GridSpan());
                    gridSpan.Val = span + additionalColumnCount;
                }
                else
                {
                    OpenXmlElement previous = cell;
                    for (var i = 0; i < additionalColumnCount; i++)
                    {
                        var blankCell = (TableCell)cell.CloneNode(true);
                        foreach (var text in blankCell.Descendants<Text>())
                        {
                            text.Text = string.Empty;
                        }

                        previous.InsertAfterSelf(blankCell);
                        previous = blankCell;
                    }
                }

                break;
            }
        }

        var tableGrid = table.GetFirstChild<TableGrid>();
        var gridColumns = tableGrid?.Elements<GridColumn>().ToList();
        if (gridColumns is null || modelColumnIndex >= gridColumns.Count)
        {
            return;
        }

        OpenXmlElement previousGridColumn = gridColumns[modelColumnIndex];
        for (var i = 0; i < additionalColumnCount; i++)
        {
            var clonedGridColumn = (GridColumn)gridColumns[modelColumnIndex].CloneNode(true);
            previousGridColumn.InsertAfterSelf(clonedGridColumn);
            previousGridColumn = clonedGridColumn;
        }
    }

    private static string GetElementText(OpenXmlElement element) =>
        string.Concat(element.Descendants<Text>().Select(x => x.Text));

    private static IReadOnlyList<Dictionary<string, string>> BuildBalanceOverviewDetailRows(
        Issue balance,
        IReadOnlyDictionary<int, Item>? itemsById,
        IReadOnlyList<SubordinateUnit> units,
        IReadOnlyDictionary<int, BalanceStockSnapshot>? stockSnapshots)
    {
        var lines = balance.Lines.OrderBy(x => x.Id).ToList();
        var rows = new List<Dictionary<string, string>>(lines.Count);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            Item? item = null;
            itemsById?.TryGetValue(line.ItemId, out item);
            var row = BuildDetailTokenMap(
                lineIndex + 1,
                line.Quantity,
                line.UnitPrice,
                line.Amount,
                line.Note,
                item);
            if (stockSnapshots?.TryGetValue(line.ItemId, out var snapshot) == true)
            {
                var totalQuantity = FormatNumber(snapshot.TotalQuantity);
                var remainingQuantity = FormatNumber(snapshot.RemainingQuantity);
                row["${SLTong}"] = totalQuantity;
                row["${SLConlai}"] = remainingQuantity;
                row["${SoLuongTong}"] = totalQuantity;
                row["${SoLuongConLai}"] = remainingQuantity;
            }
            else
            {
                row["${SLTong}"] = string.Empty;
                row["${SLConlai}"] = string.Empty;
                row["${SoLuongTong}"] = string.Empty;
                row["${SoLuongConLai}"] = string.Empty;
            }

            for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                var quantity = line.Allocations
                    .FirstOrDefault(x => x.SubordinateUnitId == units[unitIndex].Id)
                    ?.Quantity;
                row[$"${{SoLuongDonVi{unitIndex + 1}}}"] =
                    quantity is > 0 ? FormatNumber(quantity.Value) : string.Empty;
                row[$"${{SL{unitIndex + 1}}}"] =
                    quantity is > 0 ? FormatNumber(quantity.Value) : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static void ReplaceDetailTableWithBalanceOverview(
        Body body,
        Issue balance,
        IReadOnlyDictionary<int, Item>? itemsById,
        IReadOnlyList<SubordinateUnit> units)
    {
        var templateRow = body
            .Descendants<TableRow>()
            .FirstOrDefault(ContainsDetailTokens);
        if (templateRow?.Parent is not Table table)
        {
            throw new InvalidOperationException(
                "Template Word thiếu hàng chi tiết (${STT}, ${MaHang}, ...). Hãy tạo lại mẫu trong Cài đặt.");
        }

        // Giữ các hàng phía trên hàng mẫu, rồi thay bằng header + dữ liệu cân đối.
        var allRows = table.Elements<TableRow>().ToList();
        var templateIndex = allRows.IndexOf(templateRow);
        for (var i = allRows.Count - 1; i >= Math.Max(0, templateIndex - 1); i--)
        {
            allRows[i].Remove();
        }

        var headers = new List<string>
        {
            "STT", "Mã hàng", "Tên mặt hàng", "Số lượng (tổng)", "Đơn giá", "Thành tiền", "Ghi chú"
        };
        headers.AddRange(units.Select(u => u.Name));
        table.AppendChild(CreatePlainHeaderRow(headers));

        var lines = balance.Lines.OrderBy(x => x.Id).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            Item? item = null;
            itemsById?.TryGetValue(line.ItemId, out item);

            var cells = new List<string>
            {
                (i + 1).ToString(),
                item?.ItemCode ?? string.Empty,
                item?.ItemName ?? string.Empty,
                FormatNumber(line.Quantity),
                FormatNumber(line.UnitPrice),
                FormatNumber(line.Amount),
                line.Note ?? string.Empty
            };

            foreach (var unit in units)
            {
                var qty = line.Allocations.FirstOrDefault(a => a.SubordinateUnitId == unit.Id)?.Quantity;
                cells.Add(qty is > 0 ? FormatNumber(qty.Value) : string.Empty);
            }

            table.AppendChild(CreatePlainDataRow(cells));
        }
    }

    private static TableRow CreatePlainHeaderRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var text in cells)
        {
            row.AppendChild(CreatePlainCell(text, bold: true));
        }

        return row;
    }

    private static TableRow CreatePlainDataRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var text in cells)
        {
            row.AppendChild(CreatePlainCell(text, bold: false));
        }

        return row;
    }

    private static TableCell CreatePlainCell(string text, bool bold)
    {
        var runProps = new RunProperties(new FontSize { Val = "18" });
        if (bold)
        {
            runProps.AppendChild(new Bold());
        }

        return new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
            new Paragraph(new Run(runProps, new Text(text))));
    }

    private static string FormatNumber(decimal value) =>
        value.ToString("N0", VietnameseCulture);

    // Luôn sinh lại từ số tiền hiện tại. Dữ liệu cũ có thể lưu TotalAmountText
    // theo quy tắc trước đây (không có dấu phẩy giữa các nhóm số).
    private static string ResolveAmountInWords(decimal totalAmount) =>
        VietnameseMoneyTextConverter.Convert(totalAmount);

    private static string FormatDateInWords(DateTime date) =>
        $"ngày {date.Day} tháng {date.Month} năm {date.Year}";

    private static string FormatCountInWords(int count)
    {
        var numberText = VietnameseMoneyTextConverter.Convert(count);
        const string moneySuffix = " đồng";
        if (numberText.EndsWith(moneySuffix, StringComparison.OrdinalIgnoreCase))
        {
            numberText = numberText[..^moneySuffix.Length];
        }

        if (numberText.Length > 0)
        {
            var culture = CultureInfo.GetCultureInfo("vi-VN");
            numberText = char.ToLower(numberText[0], culture) + numberText[1..];
        }

        return $"{numberText} khoản";
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildReceiptDetailRows(
        Receipt receipt,
        IReadOnlyDictionary<int, Item>? itemsById)
    {
        var rows = new List<Dictionary<string, string>>(receipt.Lines.Count);
        for (var i = 0; i < receipt.Lines.Count; i++)
        {
            var line = receipt.Lines[i];
            Item? item = null;
            itemsById?.TryGetValue(line.ItemId, out item);
            rows.Add(BuildDetailTokenMap(i + 1, line.Quantity, line.UnitPrice, line.Amount, line.Note, item));
        }

        return rows;
    }

    private static IReadOnlyList<Dictionary<string, string>> BuildIssueDetailRows(
        Issue issue,
        IReadOnlyDictionary<int, Item>? itemsById)
    {
        var rows = new List<Dictionary<string, string>>(issue.Lines.Count);
        for (var i = 0; i < issue.Lines.Count; i++)
        {
            var line = issue.Lines[i];
            Item? item = null;
            itemsById?.TryGetValue(line.ItemId, out item);
            rows.Add(BuildDetailTokenMap(i + 1, line.Quantity, line.UnitPrice, line.Amount, line.Note, item));
        }

        return rows;
    }

    private static Dictionary<string, string> BuildDetailTokenMap(
        int index,
        decimal quantity,
        decimal unitPrice,
        decimal amount,
        string? note,
        Item? item)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["${STT}"] = index.ToString(),
            ["${MaHang}"] = item?.ItemCode ?? string.Empty,
            ["${TenMatHang}"] = item?.ItemName ?? string.Empty,
            ["${DanhMuc}"] = item?.Category ?? string.Empty,
            ["${DVT}"] = item?.Unit ?? string.Empty,
            ["${DonGia}"] = FormatNumber(unitPrice),
            ["${SoLuong}"] = FormatNumber(quantity),
            ["${ThanhTien}"] = FormatNumber(amount),
            ["${GhiChu}"] = note ?? string.Empty
        };
    }

    private static void RenderTemplate(
        string outputPath,
        IReadOnlyDictionary<string, string> headerTokens,
        IReadOnlyList<Dictionary<string, string>> detailRows)
    {
        using var document = WordprocessingDocument.Open(outputPath, true);
        var mainPart = document.MainDocumentPart;
        if (mainPart?.Document?.Body is not Body body)
        {
            throw new InvalidOperationException("Không đọc được nội dung file Word.");
        }

        RenderDynamicTableRows(body, detailRows);
        ReplaceTokensInDocumentParts(mainPart, body, headerTokens);
        mainPart.Document!.Save();
    }

    private static void RenderDynamicTableRows(Body body, IReadOnlyList<Dictionary<string, string>> detailRows)
    {
        var templateRows = body
            .Descendants<TableRow>()
            .Where(ContainsDetailTokens)
            .ToList();

        foreach (var templateRow in templateRows)
        {
            if (templateRow.Parent is not Table table)
            {
                continue;
            }

            foreach (var rowTokens in detailRows)
            {
                var newRow = (TableRow)templateRow.CloneNode(true);
                ReplaceTokensInContainer(newRow, rowTokens);
                table.InsertBefore(newRow, templateRow);
            }

            templateRow.Remove();
        }
    }

    private static bool ContainsDetailTokens(TableRow row)
    {
        var rowText = string.Concat(row.Descendants<Text>().Select(x => x.Text));
        return WordTemplateBuilder.DetailRowPlaceholders.Any(p =>
            rowText.Contains(p.Token, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReplaceTokensInDocumentParts(
        MainDocumentPart mainPart,
        Body body,
        IReadOnlyDictionary<string, string> tokens)
    {
        ReplaceTokensInContainer(body, tokens);
        foreach (var headerPart in mainPart.HeaderParts)
        {
            if (headerPart.Header is not null)
            {
                ReplaceTokensInContainer(headerPart.Header, tokens);
                headerPart.Header.Save();
            }
        }

        foreach (var footerPart in mainPart.FooterParts)
        {
            if (footerPart.Footer is not null)
            {
                ReplaceTokensInContainer(footerPart.Footer, tokens);
                footerPart.Footer.Save();
            }
        }
    }

    private static void ReplaceTokensInContainer(OpenXmlElement container, IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var paragraph in container.Descendants<Paragraph>())
        {
            ReplaceTokensInParagraph(paragraph, tokens);
        }
    }

    private static void ReplaceTokensInParagraph(Paragraph paragraph, IReadOnlyDictionary<string, string> tokens)
    {
        var textNodes = paragraph.Descendants<Text>().ToList();
        if (textNodes.Count == 0)
        {
            return;
        }

        var original = string.Concat(textNodes.Select(x => x.Text));
        if (string.IsNullOrEmpty(original))
        {
            return;
        }

        var matches = new List<(int Start, int Length, string Replacement)>();
        foreach (var token in tokens)
        {
            var searchFrom = 0;
            while (searchFrom < original.Length)
            {
                var start = original.IndexOf(
                    token.Key,
                    searchFrom,
                    StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    break;
                }

                matches.Add((start, token.Key.Length, token.Value ?? string.Empty));
                searchFrom = start + token.Key.Length;
            }
        }

        if (matches.Count == 0)
        {
            return;
        }

        // Gán từng ký tự gốc cho đúng Text node/Run để phần chữ xung quanh
        // placeholder giữ nguyên font, cỡ, màu, đậm/nghiêng và định dạng khác.
        var nodeAtPosition = new int[original.Length];
        var position = 0;
        for (var nodeIndex = 0; nodeIndex < textNodes.Count; nodeIndex++)
        {
            var text = textNodes[nodeIndex].Text ?? string.Empty;
            for (var i = 0; i < text.Length; i++)
            {
                nodeAtPosition[position++] = nodeIndex;
            }
        }

        var acceptedMatches = matches
            .OrderBy(x => x.Start)
            .ThenByDescending(x => x.Length)
            .Aggregate(
                new List<(int Start, int Length, string Replacement)>(),
                (accepted, candidate) =>
                {
                    if (accepted.Count == 0
                        || candidate.Start >= accepted[^1].Start + accepted[^1].Length)
                    {
                        accepted.Add(candidate);
                    }

                    return accepted;
                });

        var outputs = textNodes.Select(_ => new System.Text.StringBuilder()).ToList();
        var matchIndex = 0;
        for (var i = 0; i < original.Length;)
        {
            if (matchIndex < acceptedMatches.Count && acceptedMatches[matchIndex].Start == i)
            {
                var match = acceptedMatches[matchIndex++];
                // Nội dung thay thế nhận đúng định dạng của vị trí bắt đầu placeholder.
                outputs[nodeAtPosition[i]].Append(match.Replacement);
                i += match.Length;
                continue;
            }

            outputs[nodeAtPosition[i]].Append(original[i]);
            i++;
        }

        for (var i = 0; i < textNodes.Count; i++)
        {
            textNodes[i].Text = outputs[i].ToString();
            textNodes[i].Space = SpaceProcessingModeValues.Preserve;
        }
    }
}
