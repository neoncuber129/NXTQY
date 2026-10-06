using ClosedXML.Excel;
using Inventory.Domain.Models;
using Inventory.Reporting.Services;
using Xunit;

namespace Inventory.Tests;

public class IssueExcelWorkspaceTests
{
    [Fact]
    public void Export_Uses_Category_Instead_Of_ItemCode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcd-cat-{Guid.NewGuid():N}.xlsx");
        try
        {
            var units = new List<SubordinateUnit>
            {
                new() { Id = 1, UnitCode = "A", Name = "Đơn vị A", SortOrder = 1 }
            };
            var lines = new List<IssueExcelLine>
            {
                new("Văn phòng", "Giấy A4", 10m, 1000m, "ghi", 0m, new Dictionary<int, decimal>())
            };

            new IssueExcelWorkspaceService().ExportWorkspace(path, lines, units);

            using var wb = new XLWorkbook(path);
            var sheet = wb.Worksheet(1);
            Assert.Equal("DanhMục", sheet.Cell(1, 2).GetString());
            Assert.Equal("ĐơnGiá", sheet.Cell(1, 4).GetString());
            Assert.Equal("GhiChú", sheet.Cell(1, 5).GetString());
            Assert.Equal("TồnNgàyLậpPhiếu", sheet.Cell(1, 6).GetString());
            Assert.Equal("Văn phòng", sheet.Cell(2, 2).GetString());
            Assert.Equal("Giấy A4", sheet.Cell(2, 3).GetString());
            Assert.Equal("ghi", sheet.Cell(2, 5).GetString());
            Assert.DoesNotContain("MãHàng", sheet.Row(1).CellsUsed().Select(c => c.GetString()));
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
    public void Import_Reads_Category_And_Unit_Allocations_By_Row()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcd-imp-{Guid.NewGuid():N}.xlsx");
        try
        {
            var units = new List<SubordinateUnit>
            {
                new() { Id = 10, UnitCode = "A", Name = "Đơn vị A", SortOrder = 1 },
                new() { Id = 20, UnitCode = "B", Name = "Đơn vị B", SortOrder = 2 }
            };
            var service = new IssueExcelWorkspaceService();
            service.ExportWorkspace(
                path,
                [
                    new IssueExcelLine("DM1", "Hàng 1", 5m, 100m, null, null, new Dictionary<int, decimal>()),
                    new IssueExcelLine("DM2", "Hàng 2", 8m, 200m, null, null, new Dictionary<int, decimal>())
                ],
                units);

            // Sheet bị khóa — mở khóa để ghi số thử nghiệm.
            using (var wb = new XLWorkbook(path))
            {
                var sheet = wb.Worksheet(1);
                sheet.Unprotect("InventoryExcelWorkspace");
                sheet.Cell(2, 10).Value = 3; // đơn vị A
                sheet.Cell(2, 11).Value = 2; // đơn vị B
                sheet.Cell(3, 10).Value = 1;
                sheet.Protect("InventoryExcelWorkspace");
                wb.Save();
            }

            var result = service.ImportWorkspace(path, units);
            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
            Assert.Equal(2, result.Data.Count);
            Assert.Equal("DM1", result.Data[0].Category);
            Assert.Equal(string.Empty, result.Data[0].ItemCode);
            Assert.Equal(5m, result.Data[0].Quantity);
            Assert.Equal(3m, result.Data[0].Allocations[10]);
            Assert.Equal(2m, result.Data[0].Allocations[20]);
            Assert.Equal("DM2", result.Data[1].Category);
            Assert.Equal(1m, result.Data[1].Quantity);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
