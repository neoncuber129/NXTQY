using ClosedXML.Excel;
using Inventory.Domain.Models;

namespace Inventory.Reporting.Services;

public sealed class InventoryPeriodReportExcelExportService
{
    private static readonly string[] RomanNumerals = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX"];

    public void Export(
        string filePath,
        IReadOnlyList<PeriodStockReportRow> rows,
        DateTime fromDate,
        DateTime toDate,
        IEnumerable<string>? customCategoryOrder = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Báo cáo NXT");

        var oldDate = fromDate.AddDays(-1);
        var oldDateStr = $"{oldDate.Day}/{oldDate.Month}/{oldDate.Year}";
        var toDateStr = $"{toDate.Day}/{toDate.Month}/{toDate.Year}";

        // Thiết lập tiêu đề báo cáo ở các dòng trên (Row 1 - 2)
        sheet.Cell("A1").Value = $"BÁO CÁO SỐ LƯỢNG VÀ GIÁ TRỊ (TỪ {fromDate:dd/MM/yyyy} ĐẾN {toDate:dd/MM/yyyy})";
        sheet.Range("A1:M1").Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        // Header cấu trúc bảng đúng 13 cột như ảnh:
        // Row 4:
        sheet.Cell("A4").Value = "STT";
        sheet.Range("A4:A6").Merge();

        sheet.Cell("B4").Value = "DANH MỤC";
        sheet.Range("B4:B6").Merge();

        sheet.Cell("C4").Value = "Đơn vị\ntính";
        sheet.Range("C4:C6").Merge();

        sheet.Cell("D4").Value = "Đơn giá (1000đ)";
        sheet.Range("D4:D6").Merge();

        sheet.Cell("E4").Value = $"Tồn cũ {oldDateStr}";
        sheet.Range("E4:F5").Merge();

        sheet.Cell("G4").Value = "Số lượng tăng giảm kiểm kê";
        sheet.Range("G4:J4").Merge();

        sheet.Cell("K4").Value = $"Tồn kho cuối kỳ {toDateStr}";
        sheet.Range("K4:M5").Merge();

        // Row 5:
        sheet.Cell("G5").Value = "Tăng";
        sheet.Range("G5:H5").Merge();

        sheet.Cell("I5").Value = "Giảm";
        sheet.Range("I5:J5").Merge();

        // Row 6:
        sheet.Cell("E6").Value = "Số\nlượng";
        sheet.Cell("F6").Value = "Giá trị";
        sheet.Cell("G6").Value = "Số\nlượng";
        sheet.Cell("H6").Value = "Giá trị";
        sheet.Cell("I6").Value = "Số\nlượng";
        sheet.Cell("J6").Value = "Giá trị";
        sheet.Cell("K6").Value = "Số\nlượng";
        sheet.Cell("L6").Value = "Quy\ntrọng\nlượng";
        sheet.Cell("M6").Value = "Giá trị";

        // Row 7: Dòng đánh số cột từ 1 đến 13
        for (var c = 1; c <= 13; c++)
        {
            sheet.Cell(7, c).Value = c;
        }

        // Định dạng vùng Header (Row 4 đến Row 7)
        var headerRange = sheet.Range("A4:M7");
        headerRange.Style.Font.SetBold();
        headerRange.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        headerRange.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        headerRange.Style.Alignment.SetWrapText(true);
        headerRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        headerRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

        // Màu nền nhẹ thanh lịch cho Header
        sheet.Range("A4:M6").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F5F9"));
        sheet.Range("A7:M7").Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E2E8F0"));

        // Dữ liệu và phân nhóm danh mục
        var orderList = customCategoryOrder?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList() ?? [];

        var groups = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Category) ? "Khác" : r.Category.Trim())
            .OrderBy(g =>
            {
                var idx = orderList.FindIndex(c => string.Equals(c, g.Key, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : int.MaxValue;
            })
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentRow = 8;
        var romanIndex = 0;
        var grandOldQty = 0m;
        var grandOldAmt = 0m;
        var grandIncQty = 0m;
        var grandIncAmt = 0m;
        var grandDecQty = 0m;
        var grandDecAmt = 0m;
        var grandCloseQty = 0m;
        var grandCloseAmt = 0m;

        foreach (var group in groups)
        {
            var roman = romanIndex < RomanNumerals.Length ? RomanNumerals[romanIndex] : (romanIndex + 1).ToString();
            romanIndex++;

            // Dòng nhóm lớn cấp I: ví dụ I. THUỐC
            sheet.Cell(currentRow, 1).Value = $"{roman}.";
            sheet.Cell(currentRow, 2).Value = group.Key.ToUpper();
            sheet.Range(currentRow, 1, currentRow, 13).Style.Font.SetBold();
            sheet.Range(currentRow, 1, currentRow, 13).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F8FAFC"));
            sheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            currentRow++;

            var itemIndex = 1;

            foreach (var item in group.OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ItemCode))
            {
                sheet.Cell(currentRow, 1).Value = itemIndex++;
                sheet.Cell(currentRow, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                sheet.Cell(currentRow, 2).Value = item.ItemName;
                sheet.Cell(currentRow, 3).Value = item.Unit;
                sheet.Cell(currentRow, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                sheet.Cell(currentRow, 4).Value = item.UnitPrice;
                sheet.Cell(currentRow, 5).Value = item.OpeningQuantity;
                sheet.Cell(currentRow, 6).Value = item.OpeningAmount;
                sheet.Cell(currentRow, 7).Value = item.IncreaseQuantity;
                sheet.Cell(currentRow, 8).Value = item.IncreaseAmount;
                sheet.Cell(currentRow, 9).Value = item.DecreaseQuantity;
                sheet.Cell(currentRow, 10).Value = item.DecreaseAmount;
                sheet.Cell(currentRow, 11).Value = item.ClosingQuantity;
                // Cột 12: Quy trọng lượng - giữ cột nhưng không điền gì theo yêu cầu
                sheet.Cell(currentRow, 12).Value = string.Empty;
                sheet.Cell(currentRow, 13).Value = item.ClosingAmount;

                grandOldQty += item.OpeningQuantity;
                grandOldAmt += item.OpeningAmount;
                grandIncQty += item.IncreaseQuantity;
                grandIncAmt += item.IncreaseAmount;
                grandDecQty += item.DecreaseQuantity;
                grandDecAmt += item.DecreaseAmount;
                grandCloseQty += item.ClosingQuantity;
                grandCloseAmt += item.ClosingAmount;

                currentRow++;
            }
        }

        // Dòng TỔNG CỘNG chung
        sheet.Cell(currentRow, 2).Value = "TỔNG CỘNG";
        sheet.Range(currentRow, 1, currentRow, 13).Style.Font.SetBold().Font.SetFontSize(11);
        sheet.Range(currentRow, 1, currentRow, 13).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FEF3C7")); // Nền vàng nhạt nổi bật

        sheet.Cell(currentRow, 5).Value = grandOldQty;
        sheet.Cell(currentRow, 6).Value = grandOldAmt;
        sheet.Cell(currentRow, 7).Value = grandIncQty;
        sheet.Cell(currentRow, 8).Value = grandIncAmt;
        sheet.Cell(currentRow, 9).Value = grandDecQty;
        sheet.Cell(currentRow, 10).Value = grandDecAmt;
        sheet.Cell(currentRow, 11).Value = grandCloseQty;
        // Cột 12: Quy trọng lượng để trống
        sheet.Cell(currentRow, 12).Value = string.Empty;
        sheet.Cell(currentRow, 13).Value = grandCloseAmt;


        var lastDataRow = currentRow;

        // Định dạng border toàn bảng dữ liệu
        var tableRange = sheet.Range(4, 1, lastDataRow, 13);
        tableRange.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        tableRange.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin);

        // Định dạng số
        // Cột đơn giá (4): #,##0
        sheet.Range(8, 4, lastDataRow, 4).Style.NumberFormat.Format = "#,##0";
        // Cột số lượng (5, 7, 9, 11, 12): #,##0 hoặc #,##0.###
        sheet.Range(8, 5, lastDataRow, 5).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 7, lastDataRow, 7).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 9, lastDataRow, 9).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 11, lastDataRow, 11).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 12, lastDataRow, 12).Style.NumberFormat.Format = "#,##0";

        // Cột giá trị tiền (6, 8, 10, 13): #,##0
        sheet.Range(8, 6, lastDataRow, 6).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 8, lastDataRow, 8).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 10, lastDataRow, 10).Style.NumberFormat.Format = "#,##0";
        sheet.Range(8, 13, lastDataRow, 13).Style.NumberFormat.Format = "#,##0";

        // Thiết lập độ rộng cột
        sheet.Column(1).Width = 6;   // STT
        sheet.Column(2).Width = 36;  // DANH MỤC
        sheet.Column(3).Width = 10;  // ĐVT
        sheet.Column(4).Width = 14;  // Đơn giá
        sheet.Column(5).Width = 12;  // Tồn cũ SL
        sheet.Column(6).Width = 16;  // Tồn cũ Giá trị
        sheet.Column(7).Width = 12;  // Tăng SL
        sheet.Column(8).Width = 16;  // Tăng Giá trị
        sheet.Column(9).Width = 12;  // Giảm SL
        sheet.Column(10).Width = 16; // Giảm Giá trị
        sheet.Column(11).Width = 12; // Cuối kỳ SL
        sheet.Column(12).Width = 14; // Quy trọng lượng
        sheet.Column(13).Width = 16; // Cuối kỳ Giá trị

        sheet.ShowGridLines = true;
        workbook.SaveAs(filePath);
    }
}
