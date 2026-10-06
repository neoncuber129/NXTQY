using System.Diagnostics;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Xunit.Abstractions;

namespace Inventory.Tests;

public sealed class ReportingTests(ITestOutputHelper output)
{
    [Fact]
    public void Receipt_export_replaces_split_tokens_preserves_format_and_repeats_rows()
    {
        using var files = new TemporaryFiles();
        var template = files.PathFor("receipt-template.docx");
        var exported = files.PathFor("receipt-output.docx");
        CreateReceiptTemplate(template);

        var receipt = new Receipt
        {
            ReceiptNumber = "PN-001",
            ReceiptDate = new DateTime(2026, 7, 21),
            DeliveredBy = "Kế hoạch A",
            DeliveredUnit = "HĐ 123",
            ReceivedBy = "Thủ kho",
            Reason = "Nhập hàng",
            TotalAmount = 156_706_072,
            // Mô phỏng phiếu cũ đã lưu chữ không có dấu phẩy.
            TotalAmountText = "Một trăm năm mươi sáu triệu bảy trăm lẻ sáu nghìn không trăm bảy mươi hai đồng",
            Lines =
            [
                new ReceiptLine { ItemId = 1, Quantity = 2, UnitPrice = 1_000, Amount = 2_000 },
                new ReceiptLine { ItemId = 2, Quantity = 3, UnitPrice = 1_000, Amount = 3_000 }
            ]
        };
        var items = new Dictionary<int, Item>
        {
            [1] = new() { Id = 1, ItemCode = "MH1", ItemName = "Hàng 1", Unit = "Hộp" },
            [2] = new() { Id = 2, ItemCode = "MH2", ItemName = "Hàng 2", Unit = "Hộp" }
        };

        new WordExportService().ExportReceipt(template, exported, receipt, items);

        using var document = WordprocessingDocument.Open(exported, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Missing document body.");
        var allText = string.Concat(body.Descendants<Text>().Select(x => x.Text));
        Assert.Contains("Kế hoạch A", allText);
        Assert.Contains("HĐ 123", allText);
        Assert.Contains(
            "Một trăm năm mươi sáu triệu, bảy trăm lẻ sáu nghìn, không trăm bảy mươi hai đồng",
            allText);
        Assert.DoesNotContain("${", allText);
        var headerText = string.Concat(
            document.MainDocumentPart!.HeaderParts
                .SelectMany(x => x.Header!.Descendants<Text>())
                .Select(x => x.Text));
        Assert.Equal("Phiếu PN-001", headerText);

        var boldReplacementRun = body.Descendants<Run>()
            .Single(x => x.InnerText.Contains("Kế hoạch A", StringComparison.Ordinal));
        Assert.NotNull(boldReplacementRun.RunProperties?.Bold);

        var table = Assert.Single(body.Elements<Table>());
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(3, rows.Count); // header + two generated detail rows
        Assert.Contains("MH1", rows[1].InnerText);
        Assert.Contains("MH2", rows[2].InnerText);
    }

    [Fact]
    public void Balance_export_expands_units_and_keeps_total_in_last_column()
    {
        using var files = new TemporaryFiles();
        var template = files.PathFor("balance-template.docx");
        var exported = files.PathFor("balance-output.docx");
        CreateBalanceTemplate(template);

        var units = new[]
        {
            new SubordinateUnit { Id = 1, UnitCode = "A", Name = "Đơn vị A", SortOrder = 1 },
            new SubordinateUnit { Id = 2, UnitCode = "B", Name = "Đơn vị B", SortOrder = 2 }
        };
        var balance = new Issue
        {
            IssueNumber = "CĐ-001",
            IssueDate = new DateTime(2026, 7, 21),
            TotalAmount = 50_000,
            Lines =
            [
                new IssueLine
                {
                    Id = 1,
                    ItemId = 1,
                    Quantity = 5,
                    UnitPrice = 10_000,
                    Amount = 50_000,
                    Allocations =
                    [
                        new IssueLineAllocation { SubordinateUnitId = 1, Quantity = 2 },
                        new IssueLineAllocation { SubordinateUnitId = 2, Quantity = 3 }
                    ]
                }
            ]
        };

        new WordExportService().ExportBalanceOverview(
            template,
            exported,
            balance,
            new Dictionary<int, Item>
            {
                [1] = new() { Id = 1, ItemCode = "MH1", ItemName = "Hàng 1", Unit = "Hộp" }
            },
            units,
            new Dictionary<int, BalanceStockSnapshot>
            {
                [1] = new(10, 5)
            });

        using var document = WordprocessingDocument.Open(exported, false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Missing document body.");
        var table = Assert.Single(body.Elements<Table>());
        var rows = table.Elements<TableRow>().ToList();
        Assert.Equal(3, rows.Count);

        var headerTexts = rows[0].Elements<TableCell>().Select(x => x.InnerText).ToList();
        Assert.Contains("A", headerTexts);
        Assert.Contains("B", headerTexts);

        var detailTexts = rows[1].Elements<TableCell>().Select(x => x.InnerText).ToList();
        Assert.Contains("10", detailTexts);
        Assert.Contains("2", detailTexts);
        Assert.Contains("3", detailTexts);
        Assert.Contains("5", detailTexts);

        var footerCells = rows[2].Elements<TableCell>().ToList();
        Assert.Equal("50.000", footerCells[^1].InnerText);
        Assert.Equal(5, footerCells[0].TableCellProperties?.GridSpan?.Val?.Value);
        Assert.DoesNotContain(
            "${",
            string.Concat(table.Descendants<Text>().Select(x => x.Text)));
    }

    [Fact]
    public void Unit_issue_report_excel_contains_detail_group_and_item_sheets()
    {
        using var files = new TemporaryFiles();
        var path = files.PathFor("unit-report.xlsx");
        var rows = new[]
        {
            new UnitIssueReportRow(
                1, 10, "CD-1", new DateTime(2026, 1, 5), "PX-A1", 1,
                "A", "Đơn vị A", "Người A", "Kho", "Cấp hàng", 1, 2, 20_000),
            new UnitIssueReportRow(
                2, 11, "CD-2", new DateTime(2026, 2, 6), "PX-A2", 1,
                "A", "Đơn vị A", "Người A", "Kho", "Cấp hàng", 1, 3, 30_000),
            new UnitIssueReportRow(
                3, 12, "CD-3", new DateTime(2026, 2, 7), "PX-B1", 2,
                "B", "Đơn vị B", "Người B", "Kho", "Cấp hàng", 1, 4, 40_000)
        };
        var lines = rows.Select((row, index) => new UnitIssueReportLine(
                row.IssueId,
                row.IssueNumber,
                row.IssueDate,
                row.UnitCode,
                row.UnitName,
                index + 1,
                $"MH{index + 1}",
                "Thuốc",
                $"Mặt hàng {index + 1}",
                "Hộp",
                row.TotalQuantity,
                10_000,
                row.TotalAmount,
                null))
            .ToList();

        new InventoryExcelExportService().ExportUnitIssueReport(
            path,
            rows,
            lines,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31));

        using var workbook = new XLWorkbook(path);
        Assert.Equal(
            ["Chi tiết phiếu", "Theo đơn vị", "Theo tháng", "Chi tiết hàng hóa"],
            workbook.Worksheets.Select(x => x.Name).ToArray());
        Assert.Equal(6, workbook.Worksheet("Chi tiết phiếu").LastRowUsed()!.RowNumber());
        var unitSheet = workbook.Worksheet("Theo đơn vị");
        Assert.Equal(2, unitSheet.Cell(2, 3).GetValue<int>());
        Assert.Equal(50_000, unitSheet.Cell(2, 6).GetValue<decimal>());
        Assert.Equal(4, workbook.Worksheet("Chi tiết hàng hóa").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public void Balance_excel_places_each_unit_issue_after_balance_sheet()
    {
        using var files = new TemporaryFiles();
        var path = files.PathFor("balance-with-unit-sheets.xlsx");
        var item = new Item
        {
            Id = 1,
            ItemCode = "MH1",
            Category = "Thuốc",
            ItemName = "Mặt hàng 1",
            Unit = "Hộp"
        };
        var units = new[]
        {
            new SubordinateUnit { Id = 1, UnitCode = "A", Name = "Đơn vị A", SortOrder = 1 },
            new SubordinateUnit { Id = 2, UnitCode = "B", Name = "Đơn vị B", SortOrder = 2 }
        };
        var balance = new Issue
        {
            IssueNumber = "CD-01",
            IssueDate = new DateTime(2026, 7, 1),
            Lines =
            [
                new IssueLine { ItemId = 1, Quantity = 5, UnitPrice = 10_000, Amount = 50_000 }
            ]
        };
        var childIssues = units.Select((unit, index) => new Issue
        {
            IssueNumber = $"PX-{unit.UnitCode}",
            IssueDate = new DateTime(2026, 7, index + 2),
            ReceivedUnitCode = unit.UnitCode,
            ReceivedUnit = unit.Name,
            ReceivedBy = $"Người {unit.UnitCode}",
            Reason = "Cấp hàng",
            Lines =
            [
                new IssueLine
                {
                    ItemId = 1,
                    Quantity = index + 2,
                    UnitPrice = 10_000,
                    Amount = (index + 2) * 10_000
                }
            ]
        }).ToList();

        new InventoryExcelExportService().ExportBalance(
            path,
            balance,
            new Dictionary<int, Item> { [item.Id] = item },
            units,
            new Dictionary<int, BalanceStockSnapshot> { [item.Id] = new(10, 5) },
            childIssues);

        using var workbook = new XLWorkbook(path);
        Assert.Equal(["Sổ cân đối", "PX A", "PX B"], workbook.Worksheets.Select(x => x.Name).ToArray());
        Assert.Contains("PX-A", workbook.Worksheet("PX A").Cell(1, 1).GetString());
        Assert.Equal("MH1", workbook.Worksheet("PX A").Cell(6, 2).GetString());
        Assert.Equal(3m, workbook.Worksheet("PX B").Cell(6, 6).GetValue<decimal>());
    }

    [Fact]
    [Trait("Category", "Stress")]
    public void Dashboard_excel_exports_fifty_thousand_rows_as_valid_workbook()
    {
        using var files = new TemporaryFiles();
        var path = files.PathFor("dashboard-50k.xlsx");
        var rows = Enumerable.Range(1, 50_000)
            .Select(i => new DashboardExcelExportRow(
                $"MH{i:D5}",
                "Thuốc",
                $"Mặt hàng {i}",
                "Hộp",
                1_000 + i % 100,
                "Cấp trên",
                100,
                10,
                5,
                105,
                105_000,
                i % 10 == 0 ? "Ghi chú Unicode: Đủ" : null))
            .ToList();

        var watch = Stopwatch.StartNew();
        new DashboardExcelExportService().Export(path, rows, 2026);
        watch.Stop();
        output.WriteLine($"Excel 50k rows: {watch.Elapsed}; size: {new FileInfo(path).Length:N0} bytes");

        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet("Dashboard");
        Assert.Equal("Dashboard năm 2026", sheet.Cell(1, 1).GetString());
        Assert.Equal(50_002, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("Thuốc", sheet.Cell(3, 1).GetString());
        Assert.Equal("Mặt hàng 1", sheet.Cell(3, 2).GetString());
        Assert.Equal("Mặt hàng 50000", sheet.Cell(50_002, 2).GetString());
        Assert.Equal(105m, sheet.Cell(50_002, 9).GetValue<decimal>());
        Assert.Equal("#,##0", sheet.Cell(3, 4).Style.NumberFormat.Format);
    }

    private static void CreateReceiptTemplate(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var headerPart = main.AddNewPart<HeaderPart>();
        headerPart.Header = new Header(
            new Paragraph(new Run(new Text("Phiếu ${SoPhieu}"))));
        headerPart.Header.Save();
        var headerRelationshipId = main.GetIdOfPart(headerPart);
        var splitTokenParagraph = new Paragraph(
            new Run(
                new RunProperties(new Bold(), new Color { Val = "C00000" }),
                new Text("${Theo")),
            new Run(
                new RunProperties(new Italic()),
                new Text("KeHoach}")));
        var invoiceParagraph = new Paragraph(new Run(new Text("Hóa đơn: ${TheoHoaDonSo}")));
        var numberParagraph = new Paragraph(new Run(new Text("Số: ${SoPhieu}")));
        var amountTextParagraph = new Paragraph(new Run(new Text("${SoTienBangChu}")));
        var table = new Table(
            new TableProperties(),
            new TableGrid(
                new GridColumn(), new GridColumn(), new GridColumn(), new GridColumn()),
            Row(["STT", "Mã hàng", "Tên hàng", "SL"]),
            Row(["${STT}", "${MaHang}", "${TenMatHang}", "${SoLuong}"]));
        main.Document = new Document(new Body(
            splitTokenParagraph,
            invoiceParagraph,
            numberParagraph,
            amountTextParagraph,
            table,
            new SectionProperties(
                new HeaderReference
                {
                    Type = HeaderFooterValues.Default,
                    Id = headerRelationshipId
                })));
        main.Document.Save();
    }

    private static void CreateBalanceTemplate(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        var table = new Table(
            new TableProperties(),
            new TableGrid(
                new GridColumn(), new GridColumn(), new GridColumn(), new GridColumn(), new GridColumn()),
            Row(["Mã", "Tổng", "${Madv1}", "Còn", "Tiền"]),
            Row(["${MaHang}", "${SLTong}", "${SL1}", "${SLConlai}", "${ThanhTien}"]),
            new TableRow(
                new TableCell(
                    new TableCellProperties(new GridSpan { Val = 4 }),
                    new Paragraph(new Run(new Text("Tổng cộng")))),
                Cell("${TongTien}")));
        main.Document = new Document(new Body(table));
        main.Document.Save();
    }

    private static TableRow Row(IEnumerable<string> values) =>
        new(values.Select(Cell));

    private static TableCell Cell(string value) =>
        new(new Paragraph(new Run(new Text(value))));

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            "InventoryReportingTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryFiles() => Directory.CreateDirectory(_directory);

        public string PathFor(string fileName) => Path.Combine(_directory, fileName);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
