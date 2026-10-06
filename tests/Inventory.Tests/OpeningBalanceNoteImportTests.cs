using ClosedXML.Excel;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Xunit;

namespace Inventory.Tests;

public class OpeningBalanceNoteImportTests
{
    [Fact]
    public void OpeningTemplate_IncludesNoteColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ton-tpl-{Guid.NewGuid():N}.xlsx");
        try
        {
            new ExcelTemplateService().CreateOpeningTemplate(path);
            using var wb = new XLWorkbook(path);
            var sheet = wb.Worksheet(1);
            Assert.Equal("STT", sheet.Cell(1, 1).GetString());
            Assert.Equal("Danh mục", sheet.Cell(1, 2).GetString());
            Assert.Equal("Tên mặt hàng", sheet.Cell(1, 3).GetString());
            Assert.Equal("DVT", sheet.Cell(1, 4).GetString());
            Assert.Equal("Đơn giá", sheet.Cell(1, 5).GetString());
            Assert.Equal("Số lượng tồn đầu", sheet.Cell(1, 6).GetString());
            Assert.Equal("Loại hàng", sheet.Cell(1, 7).GetString());
            Assert.Equal("Ghi chú", sheet.Cell(1, 8).GetString());
            Assert.True(sheet.Cell(1, 9).IsEmpty());
            Assert.False(sheet.Tables.Any(), "Template đơn giản — không dùng Excel Table / Thành tiền.");
            Assert.Equal("#,##0", sheet.Column(5).Style.NumberFormat.Format);
            Assert.Equal("#,##0", sheet.Column(6).Style.NumberFormat.Format);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ImportOpeningBalances_ReadsOptionalNote()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ton-imp-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var sheet = wb.AddWorksheet("Template");
                sheet.Cell(1, 1).Value = "Danh mục";
                sheet.Cell(1, 2).Value = "Tên mặt hàng";
                sheet.Cell(1, 3).Value = "DVT";
                sheet.Cell(1, 4).Value = "Đơn giá";
                sheet.Cell(1, 5).Value = "Số lượng tồn đầu";
                sheet.Cell(1, 6).Value = "Loại hàng";
                sheet.Cell(1, 7).Value = "Ghi chú";
                sheet.Cell(1, 8).Value = "Thành tiền";
                sheet.Cell(2, 1).Value = "VP";
                sheet.Cell(2, 2).Value = "Giấy A4";
                sheet.Cell(2, 3).Value = "Ram";
                sheet.Cell(2, 4).Value = 10000;
                sheet.Cell(2, 5).Value = 5;
                sheet.Cell(2, 6).Value = "";
                sheet.Cell(2, 7).Value = "Ghi chu ton dau";
                sheet.Cell(2, 8).Value = 50000;
                wb.SaveAs(path);
            }

            var result = new ExcelImportService().ImportOpeningBalances(path);
            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
            var line = Assert.Single(result.Data);
            Assert.Equal("Ghi chu ton dau", line.Note);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task UpsertOpeningBalances_Note_AppearsOnDashboard()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                Category = "VP",
                ItemName = "Hang ton dau",
                Unit = "Hộp",
                UnitPrice = 1000,
                UsageType = "",
                OpeningQuantity = 10,
                Note = "Tu file ton dau"
            }
        ]);

        var row = Assert.Single(await database.Service.GetDashboardRowsAsync(2026));
        Assert.Equal("Tu file ton dau", row.Note);
    }
}
