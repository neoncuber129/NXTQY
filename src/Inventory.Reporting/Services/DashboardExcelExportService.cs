using ClosedXML.Excel;

namespace Inventory.Reporting.Services;

public sealed record DashboardExcelExportRow(
    string ItemCode,
    string Category,
    string ItemName,
    string Unit,
    decimal UnitPrice,
    string UsageTypeDisplay,
    decimal OpeningQuantity,
    decimal ReceivedQuantity,
    decimal IssuedQuantity,
    decimal ClosingQuantity,
    decimal Amount,
    string? Note);

public sealed class DashboardExcelExportService
{
    public void Export(string filePath, IReadOnlyList<DashboardExcelExportRow> rows, int year)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Dashboard");

        var headers = new[]
        {
            "Danh mục",
            "Tên mặt hàng",
            "DVT",
            "Đơn giá",
            "Loại sử dụng",
            "Tồn đầu năm",
            "Tổng nhập",
            "Tổng xuất",
            "Tồn cuối năm",
            "Ghi chú",
            "Thành tiền"
        };

        sheet.Cell(1, 1).Value = $"Dashboard năm {year}";
        sheet.Range(1, 1, 1, headers.Length).Merge().Style.Font.SetBold();

        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(2, c + 1).Value = headers[c];
        }

        var headerRange = sheet.Range(2, 1, 2, headers.Length);
        headerRange.Style.Font.SetBold();
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        var rowIndex = 3;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.Category;
            sheet.Cell(rowIndex, 2).Value = row.ItemName;
            sheet.Cell(rowIndex, 3).Value = row.Unit;
            sheet.Cell(rowIndex, 4).Value = row.UnitPrice;
            sheet.Cell(rowIndex, 5).Value = row.UsageTypeDisplay;
            sheet.Cell(rowIndex, 6).Value = row.OpeningQuantity;
            sheet.Cell(rowIndex, 7).Value = row.ReceivedQuantity;
            sheet.Cell(rowIndex, 8).Value = row.IssuedQuantity;
            sheet.Cell(rowIndex, 9).Value = row.ClosingQuantity;
            sheet.Cell(rowIndex, 10).Value = row.Note ?? string.Empty;
            sheet.Cell(rowIndex, 11).Value = row.Amount;
            rowIndex++;
        }

        if (rowIndex > 3)
        {
            sheet.Range(3, 4, rowIndex - 1, 9).Style.NumberFormat.Format = "#,##0";
            sheet.Range(3, 11, rowIndex - 1, 11).Style.NumberFormat.Format = "#,##0";
        }

        // AdjustToContents rất chậm với nhiều nghìn dòng — chỉ dùng khi bảng nhỏ.
        if (rows.Count <= 2000)
        {
            sheet.Columns().AdjustToContents();
        }
        else
        {
            sheet.Column(1).Width = 14;
            sheet.Column(2).Width = 28;
            sheet.Column(3).Width = 8;
            sheet.Column(4).Width = 12;
            sheet.Column(5).Width = 14;
            for (var c = 6; c <= 9; c++)
            {
                sheet.Column(c).Width = 12;
            }

            sheet.Column(10).Width = 20;
            sheet.Column(11).Width = 12;
        }

        workbook.SaveAs(filePath);
    }
}
