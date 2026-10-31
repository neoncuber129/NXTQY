using ClosedXML.Excel;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Xunit;

namespace Inventory.Tests;

public class PeriodStockReportTests
{
    [Fact]
    public async Task GetPeriodStockReportAsync_Calculates_OldStock_Increase_Decrease_And_Closing_Correctly()
    {
        await using var database = await TestDatabase.CreateAsync();

        // 1. Tồn đầu năm 2026 = 200
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Cephalexin",
                Category = "Thuốc kháng sinh",
                Unit = "Viên",
                UnitPrice = 1_300,
                UsageType = "trên cấp",
                OpeningQuantity = 200
            }
        ]);

        // 2. Nhập thêm trước kỳ báo cáo (15/09/2026): +50 viên
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-T9",
            ReceiptDate = new DateTime(2026, 9, 15),
            DeliveredBy = "Kho cấp trên",
            DeliveredUnit = "Kho Quân Y",
            ReceivedBy = "Thủ kho",
            Reason = "Nhập bổ sung tháng 9",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Cephalexin",
                    Category = "Thuốc kháng sinh",
                    Unit = "Viên",
                    UnitPrice = 1_300,
                    UsageType = "trên cấp",
                    Quantity = 50
                }
            ]
        });

        // 3. Trong kỳ báo cáo Tháng 10/2026 (01/10/2026 - 31/10/2026):
        // Nhập ngày 05/10/2026: +100 viên (Tăng)
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-T10",
            ReceiptDate = new DateTime(2026, 10, 5),
            DeliveredBy = "Kho cấp trên",
            DeliveredUnit = "Kho Quân Y",
            ReceivedBy = "Thủ kho",
            Reason = "Nhập định kỳ tháng 10",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Cephalexin",
                    Category = "Thuốc kháng sinh",
                    Unit = "Viên",
                    UnitPrice = 1_300,
                    UsageType = "trên cấp",
                    Quantity = 100
                }
            ]
        });

        // Xuất ngày 15/10/2026: -80 viên (Giảm)
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-T10",
            IssueDate = new DateTime(2026, 10, 15),
            DeliveredBy = "Thủ kho",
            ReceivedBy = "Đơn vị 1",
            ReceivedUnit = "Khoa Ngoại",
            Reason = "Cấp phát thuốc tháng 10",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Cephalexin",
                    Category = "Thuốc kháng sinh",
                    Unit = "Viên",
                    UnitPrice = 1_300,
                    UsageType = "trên cấp",
                    Quantity = 80
                }
            ]
        });

        // 4. Lấy báo cáo cho kỳ Tháng 10/2026 (01/10/2026 đến 31/10/2026)
        var filter = new PeriodStockReportFilter
        {
            Year = 2026,
            FromDate = new DateTime(2026, 10, 1),
            ToDate = new DateTime(2026, 10, 31),
            OmitZeroStockAndMovement = false
        };

        var reportRows = await database.Service.GetPeriodStockReportAsync(filter);
        var itemRow = Assert.Single(reportRows);

        Assert.Equal("Cephalexin", itemRow.ItemName);
        Assert.Equal("Viên", itemRow.Unit);
        Assert.Equal(1_300m, itemRow.UnitPrice);

        // Tồn cũ ngày 30/9/2026: 200 + 50 = 250
        Assert.Equal(250m, itemRow.OpeningQuantity);
        Assert.Equal(325_000m, itemRow.OpeningAmount); // 250 * 1,300

        // Tăng trong kỳ: 100
        Assert.Equal(100m, itemRow.IncreaseQuantity);
        Assert.Equal(130_000m, itemRow.IncreaseAmount); // 100 * 1,300

        // Giảm trong kỳ: 80
        Assert.Equal(80m, itemRow.DecreaseQuantity);
        Assert.Equal(104_000m, itemRow.DecreaseAmount); // 80 * 1,300

        // Tồn kho cuối kỳ ngày 31/10/2026: 250 + 100 - 80 = 270
        Assert.Equal(270m, itemRow.ClosingQuantity);
        Assert.Equal(270m, itemRow.ClosingWeightQuantity);
        Assert.Equal(351_000m, itemRow.ClosingAmount); // 270 * 1,300
    }

    [Fact]
    public async Task GetPeriodStockReportAsync_Supports_Year_And_Quarter_Periods()
    {
        await using var database = await TestDatabase.CreateAsync();

        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Bông gạc y tế",
                Category = "Vật tư",
                Unit = "Gói",
                UnitPrice = 10_000,
                UsageType = "tự túc",
                OpeningQuantity = 50
            }
        ]);

        // Phát sinh Quý 1 (tháng 2): Nhập 30
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-Q1",
            ReceiptDate = new DateTime(2026, 2, 10),
            DeliveredBy = "NCC A",
            DeliveredUnit = "CTY",
            ReceivedBy = "Kho",
            Reason = "Nhập vật tư",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Bông gạc y tế",
                    Category = "Vật tư",
                    Unit = "Gói",
                    UnitPrice = 10_000,
                    UsageType = "tự túc",
                    Quantity = 30
                }
            ]
        });

        // Báo cáo Quý 1/2026 (01/01/2026 - 31/03/2026)
        var q1Filter = new PeriodStockReportFilter
        {
            Year = 2026,
            FromDate = new DateTime(2026, 1, 1),
            ToDate = new DateTime(2026, 3, 31)
        };
        var q1Rows = await database.Service.GetPeriodStockReportAsync(q1Filter);
        var q1Row = Assert.Single(q1Rows);

        Assert.Equal(50m, q1Row.OpeningQuantity);
        Assert.Equal(30m, q1Row.IncreaseQuantity);
        Assert.Equal(80m, q1Row.ClosingQuantity);

        // Báo cáo Quý 2/2026 (01/04/2026 - 30/06/2026): chưa có phát sinh trong quý 2
        var q2Filter = new PeriodStockReportFilter
        {
            Year = 2026,
            FromDate = new DateTime(2026, 4, 1),
            ToDate = new DateTime(2026, 6, 30),
            OmitZeroStockAndMovement = false
        };
        var q2Rows = await database.Service.GetPeriodStockReportAsync(q2Filter);
        var q2Row = Assert.Single(q2Rows);

        // Tồn cũ ngày 31/03/2026 = 80
        Assert.Equal(80m, q2Row.OpeningQuantity);
        Assert.Equal(0m, q2Row.IncreaseQuantity);
        Assert.Equal(0m, q2Row.DecreaseQuantity);
        Assert.Equal(80m, q2Row.ClosingQuantity);
    }

    [Fact]
    public void Export_Generates_Valid_Excel_With_13_Columns_And_Expected_Headers()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"test-nxt-{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<PeriodStockReportRow>
            {
                new()
                {
                    ItemId = 1,
                    ItemCode = "CPX",
                    ItemName = "Cephalexin",
                    Category = "Thuốc kháng sinh",
                    Unit = "Viên",
                    UnitPrice = 1_300,
                    UsageType = "trên cấp",
                    OpeningQuantity = 250,
                    OpeningAmount = 325_000,
                    IncreaseQuantity = 100,
                    IncreaseAmount = 130_000,
                    DecreaseQuantity = 80,
                    DecreaseAmount = 104_000,
                    ClosingQuantity = 270,
                    ClosingWeightQuantity = 270,
                    ClosingAmount = 351_000
                }
            };

            var fromDate = new DateTime(2026, 10, 1);
            var toDate = new DateTime(2026, 10, 31);

            var exportService = new InventoryPeriodReportExcelExportService();
            exportService.Export(outputPath, rows, fromDate, toDate);

            Assert.True(File.Exists(outputPath));

            using var workbook = new XLWorkbook(outputPath);
            var sheet = workbook.Worksheet(1);

            // Kiểm tra Row 4:
            Assert.Equal("STT", sheet.Cell("A4").GetString());
            Assert.Equal("DANH MỤC", sheet.Cell("B4").GetString());
            Assert.Equal("Đơn vị\ntính", sheet.Cell("C4").GetString());
            Assert.Equal("Đơn giá (1000đ)", sheet.Cell("D4").GetString());
            Assert.Equal("Tồn cũ 30/9/2026", sheet.Cell("E4").GetString());
            Assert.Equal("Số lượng tăng giảm kiểm kê", sheet.Cell("G4").GetString());
            Assert.Equal("Tồn kho cuối kỳ 31/10/2026", sheet.Cell("K4").GetString());

            // Kiểm tra Row 5:
            Assert.Equal("Tăng", sheet.Cell("G5").GetString());
            Assert.Equal("Giảm", sheet.Cell("I5").GetString());

            // Kiểm tra Row 7 (Đánh số thứ tự 1..13):
            for (var c = 1; c <= 13; c++)
            {
                Assert.Equal(c, sheet.Cell(7, c).GetValue<int>());
            }

            // Kiểm tra Row 8 (Nhóm I. THUỐC KHÁNG SINH):
            Assert.Equal("I.", sheet.Cell(8, 1).GetString());
            Assert.Equal("THUỐC KHÁNG SINH", sheet.Cell(8, 2).GetString());

            // Kiểm tra Row 9 (Dòng chi tiết Cephalexin):
            Assert.Equal(1, sheet.Cell(9, 1).GetValue<int>());
            Assert.Equal("Cephalexin", sheet.Cell(9, 2).GetString());
            Assert.Equal("Viên", sheet.Cell(9, 3).GetString());
            Assert.Equal(1_300m, sheet.Cell(9, 4).GetValue<decimal>());
            Assert.Equal(250m, sheet.Cell(9, 5).GetValue<decimal>());
            Assert.Equal(325_000m, sheet.Cell(9, 6).GetValue<decimal>());
            Assert.Equal(100m, sheet.Cell(9, 7).GetValue<decimal>());
            Assert.Equal(130_000m, sheet.Cell(9, 8).GetValue<decimal>());
            Assert.Equal(80m, sheet.Cell(9, 9).GetValue<decimal>());
            Assert.Equal(104_000m, sheet.Cell(9, 10).GetValue<decimal>());
            Assert.Equal(270m, sheet.Cell(9, 11).GetValue<decimal>());
            // Cột 12: Quy trọng lượng để trống theo yêu cầu 3
            Assert.True(sheet.Cell(9, 12).IsEmpty() || string.IsNullOrEmpty(sheet.Cell(9, 12).GetString()));
            Assert.Equal(351_000m, sheet.Cell(9, 13).GetValue<decimal>());
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
        }
    }

    [Fact]
    public void PeriodStockReportFilter_Defaults_OmitZeroStockAndMovement_To_False()
    {
        var filter = new PeriodStockReportFilter();
        Assert.False(filter.OmitZeroStockAndMovement);
    }

    [Fact]
    public void Export_Honors_CustomCategoryOrder_In_Excel()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"test-order-{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<PeriodStockReportRow>
            {
                new()
                {
                    ItemId = 1,
                    ItemCode = "T01",
                    ItemName = "Paracetamol",
                    Category = "Thuốc giảm đau",
                    Unit = "Viên",
                    UnitPrice = 500,
                    ClosingQuantity = 100,
                    ClosingAmount = 50_000
                },
                new()
                {
                    ItemId = 2,
                    ItemCode = "V01",
                    ItemName = "Bông gòn y tế",
                    Category = "Vật tư bông băng",
                    Unit = "Gói",
                    UnitPrice = 10_000,
                    ClosingQuantity = 20,
                    ClosingAmount = 200_000
                }
            };

            var fromDate = new DateTime(2026, 10, 1);
            var toDate = new DateTime(2026, 10, 31);
            var customOrder = new[] { "Vật tư bông băng", "Thuốc giảm đau" };

            var exportService = new InventoryPeriodReportExcelExportService();
            exportService.Export(outputPath, rows, fromDate, toDate, customOrder);

            Assert.True(File.Exists(outputPath));

            using var workbook = new XLWorkbook(outputPath);
            var sheet = workbook.Worksheet(1);

            // Row 8 phải là nhóm I. VẬT TƯ BÔNG BĂNG (theo thứ tự tùy chỉnh chỉ định trước)
            Assert.Equal("I.", sheet.Cell(8, 1).GetString());
            Assert.Equal("VẬT TƯ BÔNG BĂNG", sheet.Cell(8, 2).GetString());

            // Row 9 là dòng chi tiết của Bông gòn y tế
            Assert.Equal("Bông gòn y tế", sheet.Cell(9, 2).GetString());

            // Row 10 phải là nhóm II. THUỐC GIẢM ĐAU
            Assert.Equal("II.", sheet.Cell(10, 1).GetString());
            Assert.Equal("THUỐC GIẢM ĐAU", sheet.Cell(10, 2).GetString());

            // Row 11 là dòng chi tiết của Paracetamol
            Assert.Equal("Paracetamol", sheet.Cell(11, 2).GetString());
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { }
            }
        }
    }
}
