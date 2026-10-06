using ClosedXML.Excel;

namespace Inventory.Reporting.Services;

public sealed class ExcelTemplateService
{
    private const string AppNumberFormat = "#,##0";

    public void CreateReceiptTemplate(string filePath) =>
        CreateLineTemplate(filePath, quantityHeader: "Số lượng");

    public void CreateOpeningTemplate(string filePath) =>
        CreateLineTemplate(filePath, quantityHeader: "Số lượng tồn đầu");

    public void CreateIssueTemplate(string filePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Template");
        sheet.Cell(1, 1).Value = "Mã hàng";
        sheet.Cell(1, 2).Value = "Số lượng";
        StyleHeaderRow(sheet, columnCount: 2);
        sheet.Column(2).Style.NumberFormat.Format = AppNumberFormat;
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static void CreateLineTemplate(string filePath, string quantityHeader)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Template");

        sheet.Cell(1, 1).Value = "STT";
        sheet.Cell(1, 2).Value = "Danh mục";
        sheet.Cell(1, 3).Value = "Tên mặt hàng";
        sheet.Cell(1, 4).Value = "DVT";
        sheet.Cell(1, 5).Value = "Đơn giá";
        sheet.Cell(1, 6).Value = quantityHeader;
        sheet.Cell(1, 7).Value = "Loại hàng";
        sheet.Cell(1, 8).Value = "Ghi chú";

        StyleHeaderRow(sheet, columnCount: 8);
        // Định dạng số như app — dòng mới người dùng nhập sẽ kế thừa kiểu cột.
        sheet.Column(5).Style.NumberFormat.Format = AppNumberFormat;
        sheet.Column(5).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        sheet.Column(6).Style.NumberFormat.Format = AppNumberFormat;
        sheet.Column(6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        sheet.Column(1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        sheet.ShowGridLines = true;
        sheet.Columns().AdjustToContents();
        sheet.Column(2).Width = Math.Max(5d, sheet.Column(2).Width * 0.75d);
        workbook.SaveAs(filePath);
    }

    private static void StyleHeaderRow(IXLWorksheet sheet, int columnCount)
    {
        var header = sheet.Range(1, 1, 1, columnCount);
        header.Style.Font.SetBold();
        header.Style.Font.SetFontColor(XLColor.FromHtml("#FF0F172A"));
        header.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFE2E8F0"));
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.OutsideBorderColor = XLColor.FromHtml("#FF94A3B8");
        header.Style.Border.InsideBorderColor = XLColor.FromHtml("#FFCBD5E1");
    }
}
