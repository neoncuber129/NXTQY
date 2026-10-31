using System.IO;
using Inventory.App;
using Inventory.Domain.Models;
using Xunit;

namespace Inventory.Tests;

public sealed class CarryForwardZeroStockTests
{
    [Fact]
    public async Task CarryForward_WithExcludeZeroClosingStock_HidesZeroStockInNextYearDashboard()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Năm 2025: có 3 mặt hàng
        // Mặt hàng 1: Tồn đầu 10, không xuất => tồn cuối 10
        // Mặt hàng 2: Tồn đầu 5, xuất 5 => tồn cuối 0
        // Mặt hàng 3: Tồn đầu 0, nhập 15, xuất 15 => tồn cuối 0
        await database.Service.UpsertOpeningBalancesAsync(2025,
        [
            new OpeningBalanceInput
            {
                ItemName = "Hàng còn tồn",
                Category = "Thuốc",
                Unit = "Hộp",
                UnitPrice = 10_000,
                OpeningQuantity = 10
            },
            new OpeningBalanceInput
            {
                ItemName = "Hàng xuất hết",
                Category = "Thuốc",
                Unit = "Vỉ",
                UnitPrice = 5_000,
                OpeningQuantity = 5
            }
        ]);

        // Xuất hết Hàng xuất hết (MH 2)
        var item2 = (await database.Service.GetDashboardRowsAsync(2025)).Single(x => x.ItemName == "Hàng xuất hết");
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-2025-01",
            IssueDate = new DateTime(2025, 6, 1),
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item2.ItemCode,
                    ItemName = item2.ItemName,
                    Category = item2.Category,
                    Unit = item2.Unit,
                    Quantity = 5,
                    UnitPrice = 5_000
                }
            ]
        });

        // Nhập và xuất hết MH 3
        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-2025-01",
            ReceiptDate = new DateTime(2025, 7, 1),
            DeliveredBy = "Cty",
            DeliveredUnit = "Kho",
            ReceivedBy = "Kho",
            Reason = "Nhập bổ sung",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Hàng nhập xuất hết",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 15,
                    UnitPrice = 20_000
                }
            ]
        });

        var item3Code = receipt.Lines[0].ItemId;
        var item3 = (await database.Service.GetDashboardRowsAsync(2025)).Single(x => x.ItemId == item3Code);
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-2025-02",
            IssueDate = new DateTime(2025, 8, 1),
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item3.ItemCode,
                    ItemName = item3.ItemName,
                    Category = item3.Category,
                    Unit = item3.Unit,
                    Quantity = 15,
                    UnitPrice = 20_000
                }
            ]
        });

        // Kiểm tra dashboard năm 2025: có đủ 3 mặt hàng
        var dash2025 = await database.Service.GetDashboardRowsAsync(2025);
        Assert.Equal(3, dash2025.Count);
        var r1_2025 = dash2025.Single(x => x.ItemName == "Hàng còn tồn");
        var r2_2025 = dash2025.Single(x => x.ItemName == "Hàng xuất hết");
        var r3_2025 = dash2025.Single(x => x.ItemName == "Hàng nhập xuất hết");
        Assert.Equal(10, r1_2025.ClosingQuantity);
        Assert.Equal(0, r2_2025.ClosingQuantity);
        Assert.Equal(0, r3_2025.ClosingQuantity);

        // Kiểm tra tóm tắt hoạt động năm 2025
        var activity2025 = await database.Service.GetYearStockActivityAsync(2025);
        Assert.Equal(3, activity2025.PeriodCount);
        Assert.Equal(1, activity2025.PositiveClosingCount);
        Assert.Equal(2, activity2025.ZeroClosingCount);

        // Thực hiện chuyển kỳ sang 2026 với tùy chọn excludeZeroClosingStock = true (mặc định)
        await database.Service.CarryForwardPeriodAsync(2025, 2026, overwriteExisting: true, excludeZeroClosingStock: true);

        // Kiểm tra dashboard năm 2026: CHỈ có 1 mặt hàng (Hàng còn tồn), 2 mặt hàng tồn cuối = 0 không còn xuất hiện
        var dash2026 = await database.Service.GetDashboardRowsAsync(2026);
        var single2026 = Assert.Single(dash2026);
        Assert.Equal("Hàng còn tồn", single2026.ItemName);
        Assert.Equal(10, single2026.OpeningQuantity);
        Assert.Equal(10, single2026.ClosingQuantity);

        // Kiểm tra năm 2025 xem lại: vẫn nguyên vẹn 3 mặt hàng
        var recheck2025 = await database.Service.GetDashboardRowsAsync(2025);
        Assert.Equal(3, recheck2025.Count);
    }

    [Fact]
    public async Task CarryForward_WithoutExcludeZeroClosingStock_KeepsZeroStockInNextYearDashboard()
    {
        await using var database = await TestDatabase.CreateAsync();

        // 2 mặt hàng: 1 còn tồn, 1 tồn 0
        await database.Service.UpsertOpeningBalancesAsync(2025,
        [
            new OpeningBalanceInput
            {
                ItemName = "Hàng còn",
                Category = "Thuốc",
                Unit = "Hộp",
                UnitPrice = 10_000,
                OpeningQuantity = 5
            },
            new OpeningBalanceInput
            {
                ItemName = "Hàng hết",
                Category = "Thuốc",
                Unit = "Hộp",
                UnitPrice = 5_000,
                OpeningQuantity = 0
            }
        ]);

        // Thực hiện chuyển kỳ sang 2026 với tùy chọn excludeZeroClosingStock = false (người dùng bỏ tick)
        await database.Service.CarryForwardPeriodAsync(2025, 2026, overwriteExisting: true, excludeZeroClosingStock: false);

        // Dashboard năm 2026 phải giữ cả 2 mặt hàng
        var dash2026 = await database.Service.GetDashboardRowsAsync(2026);
        Assert.Equal(2, dash2026.Count);
        Assert.Contains(dash2026, x => x.ItemName == "Hàng còn" && x.OpeningQuantity == 5);
        Assert.Contains(dash2026, x => x.ItemName == "Hàng hết" && x.OpeningQuantity == 0);
    }

    [Fact]
    public async Task CarryForward_ExcludedZeroStockItem_ReappearsWhenNewReceiptCreatedInNextYear()
    {
        await using var database = await TestDatabase.CreateAsync();

        await database.Service.UpsertOpeningBalancesAsync(2025,
        [
            new OpeningBalanceInput
            {
                ItemName = "Hàng tồn 10",
                Category = "Thuốc",
                Unit = "Hộp",
                UnitPrice = 10_000,
                OpeningQuantity = 10
            },
            new OpeningBalanceInput
            {
                ItemName = "Hàng tạm hết",
                Category = "Thuốc",
                Unit = "Lọ",
                UnitPrice = 8_000,
                OpeningQuantity = 0
            }
        ]);

        // Chuyển kỳ loại bỏ tồn = 0
        await database.Service.CarryForwardPeriodAsync(2025, 2026, overwriteExisting: true, excludeZeroClosingStock: true);

        // Năm 2026 ban đầu chỉ có Hàng tồn 10
        var dash2026Initial = await database.Service.GetDashboardRowsAsync(2026);
        Assert.Single(dash2026Initial);
        Assert.Equal("Hàng tồn 10", dash2026Initial[0].ItemName);

        // Trong năm 2026, phát sinh nhập kho cho "Hàng tạm hết"
        var itemTamHet = (await database.Service.GetDashboardRowsAsync(2025)).Single(x => x.ItemName == "Hàng tạm hết");
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-2026-01",
            ReceiptDate = new DateTime(2026, 2, 10),
            DeliveredBy = "Kho cấp trên",
            DeliveredUnit = "Cục QY",
            ReceivedBy = "Ban QY",
            Reason = "Cấp bổ sung",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = itemTamHet.ItemCode,
                    ItemName = itemTamHet.ItemName,
                    Category = itemTamHet.Category,
                    Unit = itemTamHet.Unit,
                    Quantity = 20,
                    UnitPrice = 8_000
                }
            ]
        });

        // Dashboard năm 2026 lúc này tự động xuất hiện lại "Hàng tạm hết" với số lượng nhập = 20, tồn cuối = 20
        var dash2026AfterReceipt = await database.Service.GetDashboardRowsAsync(2026);
        Assert.Equal(2, dash2026AfterReceipt.Count);
        var reappeared = dash2026AfterReceipt.Single(x => x.ItemName == "Hàng tạm hết");
        Assert.Equal(0, reappeared.OpeningQuantity);
        Assert.Equal(20, reappeared.ReceivedQuantity);
        Assert.Equal(20, reappeared.ClosingQuantity);
    }

    [Fact]
    public void UiPreferenceService_Persists_ExcludeZeroClosingStockOnCarryForward()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "nxt_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsPath = Path.Combine(tempDir, "ui-prefs.json");

            // Mặc định luôn là true
            Assert.True(UiPreferenceService.LoadExcludeZeroClosingStockOnCarryForward(settingsPath));

            // Lưu false
            UiPreferenceService.SaveExcludeZeroClosingStockOnCarryForward(false, settingsPath);
            Assert.False(UiPreferenceService.LoadExcludeZeroClosingStockOnCarryForward(settingsPath));

            // Lưu true
            UiPreferenceService.SaveExcludeZeroClosingStockOnCarryForward(true, settingsPath);
            Assert.True(UiPreferenceService.LoadExcludeZeroClosingStockOnCarryForward(settingsPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
