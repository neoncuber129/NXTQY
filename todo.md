# Tasks

- [x] Cập nhật IssueSelectableItemViewModel trong VoucherDialogViewModels.cs để thêm thuộc tính AvailableStock <!-- id: 0 -->
- [x] Cập nhật IssueDialogViewModel để cung cấp ResolveAvailabilityBatchAsync <!-- id: 1 -->
- [x] Cập nhật ItemPickerViewModel trong ItemPickerWindow.xaml.cs để tải tồn hàng loạt, hỗ trợ HideZeroStock (mặc định true) và lọc view <!-- id: 2 -->
- [x] Cập nhật ItemPickerWindow.xaml để thêm cột "Tồn ngày lập phiếu", nút checkbox "Ẩn mặt hàng tồn ngày lập phiếu = 0", và banner cảnh báo ngày tính tồn <!-- id: 3 -->
- [x] Cập nhật IssueDialogWindow.xaml.cs để truyền batch availability resolver và await nạp tồn trước khi mở ItemPickerWindow <!-- id: 4 -->
- [x] Bỏ nút "Xuất Excel" và Checkbox "Bỏ mặt hàng tồn ngày lập phiếu bằng 0" ở thanh công cụ ngoài của sổ cân đối (do đã tích hợp trực tiếp trong popup BalanceExportDialogWindow khi ấn "Xuất sổ cân đối") <!-- id: 5 -->
- [x] Thêm Checkbox "Ẩn mặt hàng không xuất gì" (Quantity = 0) trong popup BalanceExportDialogWindow, mặc định tick <!-- id: 6 -->
- [x] Ghi nhớ cả 2 trạng thái tick ("Bỏ tồn = 0" và "Ẩn không xuất") theo lần gần nhất người dùng chọn qua UiPreferenceService <!-- id: 7 -->
- [x] Viết / chạy unit tests để đảm bảo toàn bộ hệ thống hoạt động chính xác (90/90 tests pass) <!-- id: 8 -->
- [x] Cập nhật assumptions.md và hoàn thành <!-- id: 9 -->
