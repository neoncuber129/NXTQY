using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Inventory.Domain.Models;

namespace Inventory.Reporting.Services;

public sealed record WordPlaceholderInfo(string Token, string Meaning, string Scope);

/// <summary>
/// Tạo file Word mẫu chứa đầy đủ placeholder để người dùng chỉnh layout.
/// </summary>
public sealed class WordTemplateBuilder
{
    public static IReadOnlyList<WordPlaceholderInfo> ReceiptHeaderPlaceholders { get; } =
    [
        new("${SoPhieu}", "Số phiếu nhập", "Đầu phiếu nhập"),
        new("${Ngay}", "Ngày phiếu (dd/MM/yyyy)", "Đầu phiếu nhập"),
        new("${ngaychu}", "Ngày phiếu dạng: ngày ... tháng ... năm ...", "Đầu phiếu nhập"),
        new("${TheoKeHoach}", "Theo Kế hoạch", "Đầu phiếu nhập"),
        new("${TheoHoaDonSo}", "Theo Hoá đơn số", "Đầu phiếu nhập"),
        new("${NguoiNhan}", "Người nhận", "Đầu phiếu nhập"),
        new("${LyDoNhap}", "Lý do nhập", "Đầu phiếu nhập"),
        new("${TongTien}", "Tổng tiền (số)", "Đầu phiếu nhập"),
        new("${TongTienBangChu}", "Số tiền / tổng tiền bằng chữ", "Đầu phiếu nhập"),
        new("${SoTienBangChu}", "Số tiền bằng chữ (cùng giá trị với ${TongTienBangChu})", "Đầu phiếu nhập"),
        new("${SoKhoan}", "Số khoản (số dòng mặt hàng trên phiếu)", "Đầu phiếu nhập"),
        new("${sokhoanchu}", "Số khoản bằng chữ, ví dụ: mười ba khoản", "Đầu phiếu nhập")
    ];

    public static IReadOnlyList<WordPlaceholderInfo> IssueHeaderPlaceholders { get; } =
    [
        new("${SoPhieu}", "Số phiếu xuất / số phiếu cân đối", "Đầu phiếu xuất"),
        new("${Ngay}", "Ngày phiếu (dd/MM/yyyy)", "Đầu phiếu xuất"),
        new("${ngaychu}", "Ngày phiếu dạng: ngày ... tháng ... năm ...", "Đầu phiếu xuất"),
        new("${NguoiNhan}", "Người nhận (theo đơn vị trực thuộc)", "Đầu phiếu xuất"),
        new("${DonViNguoiNhan}", "Đơn vị người nhận / tên đơn vị trực thuộc", "Đầu phiếu xuất"),
        new("${MaDonVi}", "Mã đơn vị trực thuộc nhận hàng", "Đầu phiếu xuất"),
        new("${NguoiGiao}", "Người giao (theo đơn vị trực thuộc)", "Đầu phiếu xuất"),
        new("${LyDoXuat}", "Lý do xuất (theo phiếu cân đối)", "Đầu phiếu xuất"),
        new("${TongTien}", "Tổng tiền (số)", "Đầu phiếu xuất"),
        new("${TongTienBangChu}", "Số tiền / tổng tiền bằng chữ", "Đầu phiếu xuất"),
        new("${SoTienBangChu}", "Số tiền bằng chữ (cùng giá trị với ${TongTienBangChu})", "Đầu phiếu xuất"),
        new("${SoKhoan}", "Số khoản (số dòng mặt hàng trên phiếu)", "Đầu phiếu xuất"),
        new("${sokhoanchu}", "Số khoản bằng chữ, ví dụ: mười ba khoản", "Đầu phiếu xuất")
    ];

    public static IReadOnlyList<WordPlaceholderInfo> DetailRowPlaceholders { get; } =
    [
        new("${STT}", "Số thứ tự dòng", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${MaHang}", "Mã hàng", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${TenMatHang}", "Tên mặt hàng", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${DanhMuc}", "Danh mục", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${DVT}", "Đơn vị tính", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${DonGia}", "Đơn giá", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${SoLuong}", "Số lượng", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${SLTong}", "Tồn ngay trước phiếu cân đối theo ngày lập phiếu", "Dòng chi tiết phiếu cân đối"),
        new("${SLConlai}", "Tồn còn lại ngay sau phiếu cân đối", "Dòng chi tiết phiếu cân đối"),
        new("${ThanhTien}", "Thành tiền dòng", "Dòng chi tiết (trong 1 hàng bảng)"),
        new("${GhiChu}", "Ghi chú dòng", "Dòng chi tiết (trong 1 hàng bảng)")
    ];

    public static string BuildPlaceholderReferenceText()
    {
        var lines = new List<string>
        {
            "HƯỚNG DẪN PLACEHOLDER XUẤT WORD",
            "================================",
            "",
            "Cách dùng:",
            "1) Tạo mẫu Word (menu Cài đặt → Template Word).",
            "2) Mở file .docx, giữ nguyên các chuỗi ${...} đúng chính tả.",
            "3) Có thể đổi font/layout; không tách chữ placeholder thành nhiều đoạn.",
            "4) Dòng chi tiết: để đúng 1 hàng bảng chứa các ${STT}, ${MaHang}, ...",
            "   Phần mềm sẽ nhân bản hàng đó theo số dòng phiếu.",
            "",
            "--- PHIẾU NHẬP (đầu phiếu) ---"
        };

        foreach (var p in ReceiptHeaderPlaceholders)
        {
            lines.Add($"  {p.Token,-22} = {p.Meaning}");
        }

        lines.Add("");
        lines.Add("--- PHIẾU XUẤT / CÂN ĐỐI (đầu phiếu) ---");
        foreach (var p in IssueHeaderPlaceholders)
        {
            lines.Add($"  {p.Token,-22} = {p.Meaning}");
        }

        lines.Add("");
        lines.Add("--- DÒNG CHI TIẾT (chung cho nhập & xuất) ---");
        lines.Add("  Đặt các placeholder sau vào CÙNG MỘT HÀNG của bảng:");
        foreach (var p in DetailRowPlaceholders)
        {
            lines.Add($"  {p.Token,-22} = {p.Meaning}");
        }

        lines.Add("");
        lines.Add("--- PHIẾU CÂN ĐỐI — TỔNG QUAN ---");
        lines.Add("  Chỉ cần một cặp placeholder làm cột mẫu:");
        lines.Add("  ${DonVi1} = tên đơn vị; ${Madv1} = mã đơn vị.");
        lines.Add("  ${SL1} = số lượng của đơn vị.");
        lines.Add("  Các đơn vị còn lại được tự sinh sang bên phải cột mẫu.");
        lines.Add("  Vị trí cặp ô này chính là vị trí bắt đầu nhóm cột đơn vị.");
        lines.Add("  ${SLTong} = tồn trước phiếu; ${SLConlai} = tồn sau phiếu.");
        lines.Add("  Hai giá trị tồn được tính theo ngày lập phiếu cân đối.");
        lines.Add("");
        lines.Add("Lưu ý: Placeholder phân biệt đúng chữ (ví dụ ${LyDoNhap} ≠ ${LyDoXuat}).");
        return string.Join(Environment.NewLine, lines);
    }

    public void CreateReceiptTemplate(string outputPath)
    {
        CreateTemplate(
            outputPath,
            title: "MẪU PHIẾU NHẬP",
            subtitle: "Giữ nguyên các placeholder ${...}. Chỉnh layout tùy ý rồi chọn làm template trong Cài đặt.",
            headerLines:
            [
                "Số phiếu: ${SoPhieu}",
                "Ngày: ${Ngay}",
                "Ngày bằng chữ: ${ngaychu}",
                "Theo Kế hoạch: ${TheoKeHoach}",
                "Theo Hoá đơn số: ${TheoHoaDonSo}",
                "Người nhận: ${NguoiNhan}",
                "Lý do nhập: ${LyDoNhap}",
                "Tổng tiền: ${TongTien}",
                "Số tiền bằng chữ: ${SoTienBangChu}",
                "Số khoản: ${SoKhoan}",
                "Số khoản bằng chữ: ${sokhoanchu}"
            ],
            includeDetailGuide: true);
    }

    public void CreateIssueTemplate(string outputPath)
    {
        CreateIssueLikeTemplate(
            outputPath,
            title: "MẪU PHIẾU XUẤT",
            subtitle: "Mẫu riêng cho phiếu xuất theo đơn vị. Giữ nguyên placeholder ${...}.",
            unitColumnCount: 0,
            includeBalanceStockColumns: false);
    }

    public void CreateBalanceTemplate(
        string outputPath,
        IReadOnlyList<SubordinateUnit>? subordinateUnits = null)
    {
        CreateIssueLikeTemplate(
            outputPath,
            title: "MẪU PHIẾU CÂN ĐỐI",
            subtitle: "Mẫu riêng cho phiếu cân đối. Cặp placeholder đơn vị là cột mẫu.",
            unitColumnCount: subordinateUnits?.Count ?? 0,
            includeBalanceStockColumns: true);
    }

    private static void CreateIssueLikeTemplate(
        string outputPath,
        string title,
        string subtitle,
        int unitColumnCount,
        bool includeBalanceStockColumns)
    {
        CreateTemplate(
            outputPath,
            title,
            subtitle,
            headerLines:
            [
                "Số phiếu: ${SoPhieu}",
                "Ngày: ${Ngay}",
                "Ngày bằng chữ: ${ngaychu}",
                "Người nhận: ${NguoiNhan}",
                "Đơn vị người nhận: ${DonViNguoiNhan}",
                "Người giao: ${NguoiGiao}",
                "Lý do xuất: ${LyDoXuat}",
                "Tổng tiền: ${TongTien}",
                "Số tiền bằng chữ: ${SoTienBangChu}",
                "Số khoản: ${SoKhoan}",
                "Số khoản bằng chữ: ${sokhoanchu}"
            ],
            includeDetailGuide: true,
            unitColumnCount: unitColumnCount,
            includeBalanceStockColumns: includeBalanceStockColumns);
    }

    private static void CreateTemplate(
        string outputPath,
        string title,
        string subtitle,
        IReadOnlyList<string> headerLines,
        bool includeDetailGuide,
        int unitColumnCount = 0,
        bool includeBalanceStockColumns = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        var body = new Body();

        body.AppendChild(CreateParagraph(title, bold: true, fontSize: "32"));
        body.AppendChild(CreateParagraph(subtitle, bold: false, fontSize: "20"));
        body.AppendChild(CreateParagraph(string.Empty, false, "20"));

        foreach (var line in headerLines)
        {
            body.AppendChild(CreateParagraph(line, bold: false, fontSize: "22"));
        }

        body.AppendChild(CreateParagraph(string.Empty, false, "20"));
        body.AppendChild(CreateParagraph("Chi tiết hàng hóa (1 hàng mẫu — phần mềm nhân bản theo số dòng):", bold: true, fontSize: "22"));

        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

        var headerCells = new List<string>
        {
            "STT", "Mã hàng", "Tên mặt hàng", "Danh mục", "ĐVT", "Đơn giá"
        };
        var detailTokens = new List<string>
        {
            "${STT}", "${MaHang}", "${TenMatHang}", "${DanhMuc}", "${DVT}", "${DonGia}"
        };
        headerCells.Add(includeBalanceStockColumns ? "Số lượng tổng" : "Số lượng");
        detailTokens.Add(includeBalanceStockColumns ? "${SLTong}" : "${SoLuong}");
        if (unitColumnCount > 0)
        {
            headerCells.Add("${Madv1}");
            detailTokens.Add("${SL1}");
        }

        if (includeBalanceStockColumns)
        {
            headerCells.Add("Còn lại");
            detailTokens.Add("${SLConlai}");
        }

        headerCells.AddRange(["Thành tiền", "Ghi chú"]);
        detailTokens.AddRange(["${ThanhTien}", "${GhiChu}"]);
        table.AppendChild(CreateHeaderRow(headerCells));
        table.AppendChild(CreateDetailTemplateRow(detailTokens));
        body.AppendChild(table);

        if (includeDetailGuide)
        {
            body.AppendChild(CreateParagraph(string.Empty, false, "20"));
            body.AppendChild(CreateParagraph(
                "Gợi ý: Dùng ${Madv1} hoặc ${DonVi1} cùng ${SL1} để chọn vị trí bắt đầu; các đơn vị khác tự sinh sang phải.",
                bold: false,
                fontSize: "18"));
        }

        mainPart.Document = new Document(body);
        mainPart.Document.Save();
    }

    private static TableRow CreateHeaderRow(IEnumerable<string> cells)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            row.AppendChild(CreateCell(cell, bold: true));
        }

        return row;
    }

    private static TableRow CreateDetailTemplateRow(IEnumerable<string>? tokens = null)
    {
        var row = new TableRow();
        tokens ??=
        [
            "${STT}", "${MaHang}", "${TenMatHang}", "${DanhMuc}", "${DVT}",
            "${DonGia}", "${SoLuong}", "${ThanhTien}", "${GhiChu}"
        ];
        foreach (var token in tokens)
        {
            row.AppendChild(CreateCell(token, bold: false));
        }

        return row;
    }

    private static TableCell CreateCell(string text, bool bold)
    {
        return new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Auto }),
            CreateParagraph(text, bold, "18"));
    }

    private static Paragraph CreateParagraph(string text, bool bold, string fontSize)
    {
        var runProps = new RunProperties(new FontSize { Val = fontSize });
        if (bold)
        {
            runProps.AppendChild(new Bold());
        }

        return new Paragraph(new Run(runProps, new Text(text)));
    }
}
