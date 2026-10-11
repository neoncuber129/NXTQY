# Assumptions & Architecture Decisions

1. **Hiển thị bảng tick chọn mặt hàng khi xuất sổ cân đối**:
   - Khi người dùng bấm **Xuất sổ cân đối** (hoặc **Xuất Word** / **Xuất Excel** của phiếu cân đối), ứng dụng hiển thị hộp thoại `BalanceExportDialogWindow`.
   - Hộp thoại này chứa:
     - Checkbox **"Bỏ mặt hàng tồn ngày lập phiếu bằng 0"**: Mặc định được tick (`true`).
     - Bảng danh sách (DataGrid) chi tiết các mặt hàng:
       - Các mặt hàng có `Tồn ngày lập phiếu > 0`: Mặc định được tick chọn (`IsSelected = true`).
       - Các mặt hàng có `Tồn ngày lập phiếu <= 0`: Mặc định không được tick chọn (`IsSelected = false`).
     - Thanh tìm kiếm mặt hàng (theo mã, tên, danh mục).
     - Nút "Chọn tất cả" và "Bỏ chọn tất cả".
     - Dòng tóm tắt tự động cập nhật: số lượng mặt hàng đã tick / tổng số mặt hàng, kèm tổng tiền của các mặt hàng được chọn.
     - Hai nút xuất trực tiếp: **Xuất Word** và **Xuất Excel**, kèm nút **Đóng**.

2. **Cơ chế lọc và đánh số liên tục**:
   - Các dịch vụ `WordExportService` và `InventoryExcelExportService` hỗ trợ xuất theo tập ID dòng hàng được tick (`selectedLineIds`).
   - STT trong bảng xuất được tự động đánh số liên tục từ 1.
   - Các biến tổng quan (`${SoKhoan}`, `${sokhoanchu}`, `${TongTien}`, `${TongTienBangChu}`, ô Tổng tiền trên Excel) khớp chính xác với những mặt hàng được tick chọn.

3. **Ghi nhớ cấu hình (User Preferences)**:
   - Trạng thái của Checkbox "Bỏ mặt hàng tồn ngày lập phiếu bằng 0" được lưu và đọc qua `UiPreferenceService`, mặc định luôn là `true`.

4. **Popup chọn mặt hàng đưa vào sổ cân đối (`ItemPickerWindow`)**:
   - **Cột hiển thị tồn**: Thêm cột `Tồn ngày lập phiếu` (định dạng `N0`, in đậm) vào cả hai tab (Danh sách đầy đủ và Theo phiếu nhập). Bổ sung đầy đủ cột `Tên mặt hàng` và `ĐVT` để tiện theo dõi.
   - **Tùy chọn ẩn tồn = 0**: Checkbox `Ẩn mặt hàng tồn ngày lập phiếu = 0` được đặt ở thanh trên cùng, **mặc định BẬT (`true`)**. Khi thay đổi trạng thái, danh sách mặt hàng tự động làm mới ngay lập tức.
   - **Cảnh báo ngày tính tồn**: 
     - Hiển thị banner cảnh báo màu vàng nổi bật ngay trên cùng: `⚠️ Lưu ý: Tồn ngày lập phiếu được tính đến ngày {dd/MM/yyyy} (ngày lập phiếu đã chọn).`
     - Khi người dùng bỏ tick ẩn tồn = 0 và bấm "Thêm mặt hàng đã chọn" có chứa mặt hàng tồn <= 0, hệ thống hiển thị thêm hộp thoại cảnh báo xác nhận trước khi đưa vào phiếu cân đối.
   - **Hiệu năng tải tồn**: Sử dụng batch availability resolver (`ResolveAvailabilityBatchAsync`) nạp tồn hàng loạt tức thì trước khi hiển thị popup, không gây giật lag.

5. **Lược bỏ nút Xuất Excel và Checkbox bỏ tồn = 0 ở thanh công cụ ngoài**:
   - Vì hộp thoại `BalanceExportDialogWindow` (khi nhấn nút **Xuất sổ cân đối**) đã tích hợp đầy đủ cả hai tùy chọn: xuất ra Word hoặc xuất ra Excel, cùng checkbox bỏ tồn ngày lập phiếu = 0 và bảng tick từng dòng mặt hàng, nên nút "Xuất Excel" và checkbox "Bỏ mặt hàng tồn ngày lập phiếu bằng 0 khi xuất sổ" ở thanh công cụ bên ngoài (tại `MainWindow.xaml` và `BalanceIssueDetailWindow.xaml`) đã được lược bỏ để giao diện gọn gàng, trực quan và không bị trùng lặp chức năng.
