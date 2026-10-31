# Assumptions & Architecture Decisions

1. **Hiển thị bảng tick chọn mặt hàng khi xuất sổ cân đối**:
   - Khi người dùng bấm **Xuất sổ cân đối** (hoặc **Xuất Word** / **Xuất Excel** của phiếu cân đối), ứng dụng hiển thị hộp thoại `BalanceExportDialogWindow`.
   - Hộp thoại này chứa:
     - Checkbox **"Bỏ mặt hàng tồn ngày lập phiếu bằng 0"**: Mặc định được tick (`true`).
     - Checkbox **"Ẩn mặt hàng không xuất gì"**: Mặc định được tick (`true`).
     - Bảng danh sách (DataGrid) chi tiết các mặt hàng:
       - Các mặt hàng có `Tồn ngày lập phiếu > 0` và `Số lượng xuất > 0`: Mặc định được tick chọn (`IsSelected = true`).
       - Các mặt hàng có `Tồn ngày lập phiếu <= 0` hoặc `Số lượng xuất <= 0`: Mặc định không được tick chọn (`IsSelected = false`).
     - Thanh tìm kiếm mặt hàng (theo mã, tên, danh mục).
     - Nút "Chọn tất cả" và "Bỏ chọn tất cả".
     - Dòng tóm tắt tự động cập nhật: số lượng mặt hàng đã tick / tổng số mặt hàng, kèm tổng tiền của các mặt hàng được chọn.
     - Hai nút xuất trực tiếp: **Xuất Word** và **Xuất Excel**, kèm nút **Đóng**.

2. **Cơ chế lọc và đánh số liên tục**:
   - Các dịch vụ `WordExportService` và `InventoryExcelExportService` hỗ trợ xuất theo tập ID dòng hàng được tick (`selectedLineIds`).
   - STT trong bảng xuất được tự động đánh số liên tục từ 1.
   - Các biến tổng quan (`${SoKhoan}`, `${sokhoanchu}`, `${TongTien}`, `${TongTienBangChu}`, ô Tổng tiền trên Excel) khớp chính xác với những mặt hàng được tick chọn.

3. **Ghi nhớ cấu hình (User Preferences)**:
   - Trạng thái của cả 2 Checkbox:
     - "Bỏ mặt hàng tồn ngày lập phiếu bằng 0" (`OmitZeroStockAtIssueDate`).
     - "Ẩn mặt hàng không xuất gì" (`HideZeroQuantityBalanceExport`).
   - Cả 2 trạng thái này được tự động lưu lại mỗi khi người dùng tick hoặc bỏ tick, và được nạp lại chính xác ở lần mở popup gần nhất. Mặc định ban đầu luôn là `true`.

4. **Popup chọn mặt hàng đưa vào sổ cân đối (`ItemPickerWindow`)**:
   - **Cột hiển thị tồn**: Thêm cột `Tồn ngày lập phiếu` (định dạng `N0`, in đậm) vào cả hai tab (Danh sách đầy đủ và Theo phiếu nhập). Bổ sung đầy đủ cột `Tên mặt hàng` và `ĐVT` để tiện theo dõi.
   - **Tùy chọn ẩn tồn = 0**: Checkbox `Ẩn mặt hàng tồn ngày lập phiếu = 0` được đặt ở thanh trên cùng, **mặc định BẬT (`true`)**. Khi thay đổi trạng thái, danh sách mặt hàng tự động làm mới ngay lập tức.
   - **Cảnh báo ngày tính tồn**: 
     - Hiển thị banner cảnh báo màu vàng nổi bật ngay trên cùng: `⚠️ Lưu ý: Tồn ngày lập phiếu được tính đến ngày {dd/MM/yyyy} (ngày lập phiếu đã chọn).`
     - Khi người dùng bỏ tick ẩn tồn = 0 và bấm "Thêm mặt hàng đã chọn" có chứa mặt hàng tồn <= 0, hệ thống hiển thị thêm hộp thoại cảnh báo xác nhận trước khi đưa vào phiếu cân đối.
   - **Hiệu năng tải tồn**: Sử dụng batch availability resolver (`ResolveAvailabilityBatchAsync`) nạp tồn hàng loạt tức thì trước khi hiển thị popup, không gây giật lag.

5. **Lược bỏ nút Xuất Excel và Checkbox bỏ tồn = 0 ở thanh công cụ ngoài**:
   - Vì hộp thoại `BalanceExportDialogWindow` (khi nhấn nút **Xuất sổ cân đối**) đã tích hợp đầy đủ cả hai tùy chọn: xuất ra Word hoặc xuất ra Excel, cùng checkbox bỏ tồn ngày lập phiếu = 0, checkbox ẩn mặt hàng không xuất gì và bảng tick từng dòng mặt hàng, nên nút "Xuất Excel" và checkbox thừa ở thanh công cụ bên ngoài đã được lược bỏ để giao diện gọn gàng, trực quan và không bị trùng lặp chức năng.

6. **Tuỳ chọn ẩn mặt hàng tồn cuối năm trước bằng 0 khi Chuyển kỳ (Carry Forward)**:
   - **Yêu cầu người dùng**: Khi thực hiện chuyển kỳ, thêm tùy chọn checkbox "những mặt hàng tồn cuối năm trước là 0 thì năm sau sẽ không còn trong dashboard nữa", mặc định chọn.
   - **Mô hình dữ liệu**:
     - Thêm thuộc tính `IsHiddenInDashboard` (kiểu boolean, mặc định `false`) vào thực thể `StockPeriod` (được đánh index theo `[Year, ItemId]`).
     - Tự động bổ sung cột `IsHiddenInDashboard` vào SQLite qua Migration `20261025110000_AddStockPeriodIsHiddenInDashboard` và cơ chế self-heal `EnsureSchemaPatchesAsync`.
   - **Logic Chuyển kỳ (`CarryForwardPeriodAsync`)**:
     - Khi tùy chọn `excludeZeroClosingStock` được bật (`true` - mặc định):
       - Các mặt hàng có số tồn cuối năm nguồn bằng 0 (hoặc không phát sinh tồn ở năm nguồn) sẽ được gán `IsHiddenInDashboard = true` cho kỳ năm đích.
       - Các mặt hàng có tồn cuối > 0 sẽ được chuyển thành tồn đầu năm đích và gán `IsHiddenInDashboard = false`.
     - Khi tùy chọn tắt (`false`): Tất cả mặt hàng đều được gán `IsHiddenInDashboard = false`.
   - **Hiển thị Dashboard (`GetDashboardRowsAsync`)**:
     - Mặt hàng có `IsHiddenInDashboard == true` và không có số lượng tồn/nhập/xuất trong năm sẽ được loại bỏ khỏi danh sách dòng hiển thị trên Dashboard của năm đích.
     - Dữ liệu năm nguồn (và các năm trước đó) vẫn hiển thị đầy đủ, không bị ảnh hưởng.
     - Nếu trong năm đích, mặt hàng được lập phiếu nhập mới (`CreateReceiptAsync`), hệ thống tự động gán `IsHiddenInDashboard = false` để mặt hàng tự động tái xuất hiện trên Dashboard năm đích ngay khi có hàng.
   - **Giao diện & Trải nghiệm người dùng**:
     - Hộp thoại `CarryForwardWindow` hiển thị CheckBox: `"Không chuyển mặt hàng tồn cuối bằng 0 sang Dashboard năm sau (mặc định)"` (mặc định được tick).
     - Phần tóm tắt năm nguồn hiển thị chi tiết số lượng mặt hàng có tồn cuối > 0 và số lượng mặt hàng tồn cuối = 0 để người dùng nắm rõ.
     - Trạng thái tick được ghi nhớ qua `UiPreferenceService.LoadExcludeZeroClosingStockOnCarryForward()` / `SaveExcludeZeroClosingStockOnCarryForward()`.

7. **Đảm bảo tính toàn vẹn tính toán khi Nhập / Xuất / Chuyển kỳ / Sửa tồn đầu qua nhiều năm**:
   - **Bảo toàn dòng thời gian (Stock Timeline Invariant)**:
     - Xây dựng hàm `ValidateTimelineStockAsync`: kiểm tra lũy kế số dư tồn theo từng mốc thời gian từ đầu năm đến hết năm. Nếu bất kỳ mốc thời gian nào sau một phiếu xuất bị âm, hệ thống chặn ngay lập tức và thông báo chi tiết: mặt hàng, ngày bị âm, số tồn khả dụng và số yêu cầu xuất.
   - **Chặn lập phiếu xuất lùi ngày gây âm kho**:
     - `EnsureSufficientStockAsync` kiểm tra dòng thời gian toàn bộ năm. Việc lập phiếu xuất lùi ngày vào giữa kỳ nếu làm âm kho các phiếu xuất ở các ngày sau đó sẽ bị từ chối.
   - **Bảo vệ an toàn khi xóa/sửa phiếu nhập**:
     - `DeleteReceiptAsync`: Kiểm tra xem lượng hàng nhập từ phiếu này đã bị xuất dùng ở các phiếu xuất sau đó chưa. Nếu đã xuất dùng, chặn xóa phiếu nhập.
     - `UpdateReceiptAsync`: Kiểm tra nếu giảm số lượng nhập hoặc lùi ngày nhập làm âm các phiếu xuất sau đó thì transaction sẽ tự động rollback an toàn.
   - **Tự động cập nhật sổ chi tiết và chặn âm kho khi sửa tồn đầu năm**:
     - `UpsertOpeningBalancesAsync`: Kiểm tra xem việc giảm tồn đầu có gây thiếu hàng cho bất kỳ phiếu xuất nào trong năm không. Sau khi lưu tồn đầu, tự động gọi `RebuildLedgerBalancesAsync` để cập nhật lại toàn bộ cột `BalanceAfter` trong sổ cái chi tiết.
   - **Cơ chế Chuyển kỳ an toàn khi năm đích đã có phát sinh**:
     - `CarryForwardPeriodAsync`: Bổ sung tham số `allowUpdateOpeningWithExistingActivity`.
     - Khi bật tùy chọn này trên giao diện `CarryForwardWindow`: cho phép người dùng đồng bộ lại tồn đầu năm đích từ tồn cuối năm nguồn dù năm đích đã có chứng từ. Hệ thống bảo toàn 100% các phiếu nhập/xuất đã lập của năm đích, chỉ cập nhật `OpeningQuantity`, tính lại `ClosingQuantity`, kiểm tra an toàn dòng thời gian và tự động đồng bộ lại toàn bộ số dư sổ chi tiết `StockLedgerEntries`.
   - **Ưu tiên đơn giá kỳ tồn trong Dashboard**:
     - `GetDashboardRowsAsync`: Ưu tiên sử dụng `StockPeriod.UnitPrice` (nếu > 0) để tính thành tiền tồn kho (`Amount = ClosingQuantity * UnitPrice`), phản ánh đúng đơn giá riêng biệt của từng niên độ kế toán.
   - **Bảo toàn danh mục hàng hóa khi xóa phiếu nhập**:
     - `RemoveUnusedItemsAsync`: Chỉ xóa item mồ côi nếu nó chỉ xuất hiện trên duy nhất một phiếu nhập vừa bị xóa và chưa từng có mặt ở các năm khác (`periods.Count <= 1`). Nếu mặt hàng đã tồn tại ở nhiều năm hoặc có ghi chú cấu hình, danh mục hàng sẽ được bảo toàn nguyên vẹn.

8. **Chức năng Báo cáo số lượng và giá trị theo Tháng, Quý, Năm (Báo cáo NXT)**:
   - **Bám sát cấu trúc bảng mẫu 13 cột theo đúng ảnh người dùng cung cấp**:
     - Cột 1: `STT` (đánh số thứ tự).
     - Cột 2: `DANH MỤC` (phân nhóm danh mục cấp I: `I. THUỐC`, `II. VẬT TƯ`... và tên mặt hàng chi tiết).
     - Cột 3: `Đơn vị tính` (ĐVT).
     - Cột 4: `Đơn giá (1000đ)` (chuẩn theo tiêu đề biểu mẫu).
     - Cột 5-6: `Tồn cũ [ngày trước kỳ]` (Ví dụ kỳ 10/2026 thì hiển thị `Tồn cũ 30/9/2026`), gồm Số lượng và Giá trị.
     - Cột 7-10: `Số lượng tăng giảm kiểm kê`:
       - Tăng (Nhập trong kỳ): gồm Số lượng và Giá trị.
       - Giảm (Xuất trong kỳ): gồm Số lượng và Giá trị.
     - Cột 11-13: `Tồn kho cuối kỳ [ngày kết thúc kỳ]` (Ví dụ: `Tồn kho cuối kỳ 31/10/2026`), gồm Số lượng, Quy trọng lượng và Giá trị.
     - Dòng 7: Đánh số thứ tự các cột từ `1` đến `13`.
     - Dòng nhóm và dòng Cộng từng nhóm danh mục, dòng **TỔNG CỘNG** toàn kho ở cuối bảng.
   - **Quy tắc tính toán tồn cũ và phát sinh theo kỳ [FromDate, ToDate]**:
     - $\text{Tồn cũ} = \text{Tồn đầu năm} + \text{Nhập trước kỳ trong năm} - \text{Xuất trước kỳ trong năm}$.
     - $\text{Giá trị tồn cũ} = \text{Tồn cũ} \times \text{Đơn giá}$.
     - $\text{Tăng (SL)} = \sum \text{ReceiptLine.Quantity}$ trong kỳ [FromDate, ToDate]; $\text{Tăng (Giá trị)} = \text{Tăng (SL)} \times \text{Đơn giá}$.
     - $\text{Giảm (SL)} = \sum \text{IssueLine.Quantity}$ (`ParentIssueId == null`) trong kỳ [FromDate, ToDate]; $\text{Giảm (Giá trị)} = \text{Giảm (SL)} \times \text{Đơn giá}$.
     - $\text{Tồn cuối (SL)} = \text{Tồn cũ} + \text{Tăng (SL)} - \text{Giảm (SL)}$.
     - $\text{Quy trọng lượng} = \text{Tồn cuối (SL)}$.
     - $\text{Tồn cuối (Giá trị)} = \text{Tồn cuối (SL)} \times \text{Đơn giá}$.
   - **Lược bỏ ô nhập Tên nguồn tùy chỉnh**:
     - Theo yêu cầu của người dùng, lược bỏ ô nhập tùy chỉnh Tên nguồn để giao diện tinh gọn, tập trung chuẩn vào cấu trúc bảng 13 cột.
   - **Hỗ trợ đầy đủ các kỳ thời gian**:
     - **Theo Tháng**: Chọn Năm và Tháng (1 đến 12), tự động xác định ngày đầu tháng và cuối tháng, header tồn cũ là ngày cuối tháng trước.
     - **Theo Quý**: Chọn Quý I, II, III, IV, tự động xác định ngày đầu quý và cuối quý.
     - **Theo Năm**: Chọn Năm, tự động xác định từ 01/01 đến 31/12.
     - **Tùy chọn ngày**: Cho phép nhập ngày linh hoạt.
   - **Xuất file Excel chuyên nghiệp**:
     - Dịch vụ `InventoryPeriodReportExcelExportService` sử dụng ClosedXML để xuất bảng 13 cột chuẩn mực, hợp nhất ô (merge cells) đa tầng, viền mỏng thanh lịch, định dạng số tiền `#,#0` và số lượng `#,#0`.

9. **Đồng bộ hóa giao diện phần mềm chuẩn mẫu Excel & Cải tiến bộ chọn kỳ trực quan** (Yêu cầu cập nhật mới):
   - **Giao diện bộ chọn kỳ trực quan**:
     - Thay thế các ComboBox chọn kỳ bằng thanh chuyển đổi chế độ Pill Buttons (`[ 📅 Theo Tháng ]`, `[ 📊 Theo Quý ]`, `[ 📆 Theo Năm ]`, `[ ⏱ Tùy chọn ngày ]`) có trạng thái kích hoạt đổi màu xanh nổi bật.
     - Tích hợp điều hướng năm nhanh `[ ◀ ] [ Năm ] [ ▶ ]`.
     - Lựa chọn tháng trực quan với 12 nút tháng từ `Th.1` đến `Th.12` (tháng đang chọn được highlight).
     - Lựa chọn quý trực quan với 4 nút quý `Quý I (T1-T3)`, `Quý II (T4-T6)`, `Quý III (T7-T9)`, `Quý IV (T10-T12)`.
     - Chế độ tùy chọn khoảng ngày có các nút chọn nhanh `[ Tháng này ]`, `[ Quý này ]`, `[ Năm nay ]`.
     - Huy hiệu tóm tắt kỳ báo cáo hiển thị rõ ràng thời gian thực (ví dụ: `📌 Kỳ báo cáo: Tháng 05/2026 (01/05/2026 - 31/05/2026)`).
   - **Đồng nhất 100% giao diện phần mềm với mẫu Excel**:
     - Thiết kế tiêu đề bảng đa tầng (Multi-Tier Header) 13 cột kết hợp dòng đánh số `(1)` đến `(13)` y hệt như file Excel.
     - Dữ liệu hiển thị qua `PeriodReportDisplayRow` có các dòng phân nhóm lớn cấp La Mã (ví dụ: `I. THUỐC`, `II. VẬT TƯ Y TẾ`...) với nền xám nhạt `#F1F5F9`, chữ in hoa đậm.
     - Dòng `TỔNG CỘNG` ở cuối bảng nổi bật với nền vàng nhạt `#FEF3C7`, chữ đậm màu `#92400E`.
   - **Bỏ quy trọng lượng (Cột 12)**:
     - Vẫn duy trì cột số 12 "Quy trọng lượng" trên cả giao diện phần mềm lẫn file Excel xuất ra để giữ đúng kết cấu 13 cột chuẩn của biểu mẫu, nhưng toàn bộ giá trị trong cột được để trống hoàn toàn (chuỗi rỗng).
   - **Lược bỏ dòng cộng dưới từng danh mục**:
     - Không sinh dòng cộng nhóm con ở dưới mỗi danh mục, bảng dữ liệu chạy liền mạch từ nhóm này sang nhóm khác và chỉ có duy nhất 1 dòng TỔNG CỘNG ở cuối bảng.

10. **Chuẩn hóa kiến trúc giao diện WPF chuẩn mực (Native WPF DataGrid)**:
    - Loại bỏ hoàn toàn khối `Grid` tiêu đề rời bên ngoài và khối `ScrollViewer` bọc ngoài gây lỗi co rút cột DataGrid (khiến toàn bộ dữ liệu bị dồn ép sang góc trái và lệch hoàn toàn khỏi tiêu đề).
    - Áp dụng cấu trúc **DataGrid chuẩn WPF**: Các tiêu đề cột được tích hợp trực tiếp vào từng `DataGridColumn.Header` với bố cục StackPanel gồm Nhóm nghiệp vụ (ví dụ: `Tồn cũ 30/9/2026`, `TĂNG (NHẬP KỲ)`, `GIẢM (XUẤT KỲ)`, `Tồn kho cuối kỳ 31/10/2026`), Phân loại (`Số lượng` / `Thành tiền`) và số thứ tự cột chuẩn `(1)` đến `(13)`.
    - Tiêu đề và ô dữ liệu liên kết 1:1, tự động đồng bộ chiều rộng, hỗ trợ cuộn mượt mà (Horizontal & Vertical Virtualization) và co giãn tự nhiên theo kích thước màn hình mà không bao giờ bị lệch.
    - Sửa lỗi co cụm/cắt chữ (vertical clipping) của 2 ComboBox `Nguồn:` và `Danh mục:`.

11. **Mặc định không chọn ẩn tồn & phát sinh = 0 và Tùy chỉnh thứ tự nhóm danh mục**:
    - **Mặc định không chọn "Ẩn tồn và phát sinh = 0"**:
      - `PeriodStockReportFilter.OmitZeroStockAndMovement` và thuộc tính ViewModel `ReportOmitZeroStock` được đặt mặc định là `false` (không tick). Người dùng có thể chủ động tick khi muốn lọc gọn danh sách.
    - **Nút "↕ Sắp xếp nhóm" trên giao diện**:
      - Được bố trí ngay cạnh ComboBox `Danh mục:` ở thanh công cụ bộ lọc của tab Báo cáo NXT, có tooltip hướng dẫn và style WPF chuẩn mực.
    - **Cửa sổ WPF chuẩn UI/UX `CategoryOrderWindow`**:
      - Hiển thị danh sách nhóm danh mục trực quan kèm huy hiệu số La Mã tương ứng (`I.`, `II.`, `III.`...).
      - Trang bị đầy đủ các nút di chuyển: `▲ Di chuyển lên` (hỗ trợ phím tắt `Alt+Up`), `▼ Di chuyển xuống` (hỗ trợ phím tắt `Alt+Down`), `⏫ Lên đầu tiên`, `⏬ Xuống cuối cùng`, `🔤 Sắp xếp A → Z` và `↺ Mặc định ban đầu`.
      - Tự động bật/tắt (Enable/Disable) các nút di chuyển theo vị trí dòng đang chọn trong danh sách.
    - **Lưu trữ & Áp dụng đồng bộ**:
      - Thứ tự sắp xếp được lưu trữ bền vững qua `UiPreferenceService.SaveReportCategoryOrder` / `LoadReportCategoryOrder` (trong file `ui-preferences.json`).
      - Khi bấm **Áp dụng & Lưu**, bảng dữ liệu trên màn hình tự động làm mới tức thì mà không cần truy vấn lại cơ sở dữ liệu.
      - File Excel xuất ra (`InventoryPeriodReportExcelExportService`) cũng đồng bộ 100% theo đúng thứ tự nhóm danh mục đã tùy chỉnh.

