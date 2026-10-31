using Inventory.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Tests;

public class MultiYearCalculationIntegrityTests
{
    [Fact]
    public async Task Retroactive_Issue_Cannot_Cause_Future_Negative_Stock()
    {
        await using var database = await TestDatabase.CreateAsync();

        // 1. Nhập 10 cái ngày 01/02/2026
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-01",
            ReceiptDate = new DateTime(2026, 2, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập ban đầu",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-01",
                    ItemName = "Vật tư 01",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 10,
                    UnitPrice = 50_000
                }
            ]
        });

        // 2. Xuất 10 cái ngày 15/02/2026 (tồn kho từ ngày 15/02 trở đi = 0)
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-01",
            IssueDate = new DateTime(2026, 2, 15),
            ReceivedBy = "Đơn vị 1",
            Reason = "Xuất hết",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-01",
                    ItemName = "Vật tư 01",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 10,
                    UnitPrice = 50_000
                }
            ]
        });

        // 3. Cố tình lập phiếu xuất lùi ngày vào ngày 05/02/2026 với số lượng 5
        // Tại ngày 05/02 thì tồn có 10, nhưng nếu xuất 5 thì đến ngày 15/02 sẽ bị âm 5!
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.CreateIssueAsync(new IssueInput
            {
                IssueNumber = "PX-RETRO",
                IssueDate = new DateTime(2026, 2, 5),
                ReceivedBy = "Đơn vị 2",
                Reason = "Xuất lùi ngày",
                Lines =
                [
                    new TransactionLineInput
                    {
                        ItemCode = "VT-01",
                        ItemName = "Vật tư 01",
                        Category = "Vật tư",
                        Unit = "Cái",
                        Quantity = 5,
                        UnitPrice = 50_000
                    }
                ]
            }));

        Assert.Contains("không đủ tồn tại ngày 15/02/2026", ex.Message);
    }

    [Fact]
    public async Task DeleteReceipt_Blocked_If_Stock_Used_In_Future_Issue()
    {
        await using var database = await TestDatabase.CreateAsync();

        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-DEL-TEST",
            ReceiptDate = new DateTime(2026, 1, 10),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập hàng",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-DEL",
                    ItemName = "Vật tư thử xóa",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 10,
                    UnitPrice = 20_000
                }
            ]
        });

        // Xuất dùng 6 cái vào ngày 15/01/2026
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-DEL-TEST",
            IssueDate = new DateTime(2026, 1, 15),
            ReceivedBy = "Kho A",
            Reason = "Xuất dùng",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-DEL",
                    ItemName = "Vật tư thử xóa",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 6,
                    UnitPrice = 20_000
                }
            ]
        });

        // Cố tình xóa phiếu nhập ban đầu -> Phải bị chặn vì hàng đã xuất
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.DeleteReceiptAsync(receipt.Id));

        Assert.Contains("không đủ tồn", ex.Message);

        // Đảm bảo phiếu nhập vẫn còn nguyên
        await using var context = database.CreateContext();
        Assert.NotNull(await context.Receipts.FindAsync(receipt.Id));
    }

    [Fact]
    public async Task UpdateReceipt_Blocked_If_Reduced_Quantity_Causes_Negative_Stock()
    {
        await using var database = await TestDatabase.CreateAsync();

        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-UPDATE-TEST",
            ReceiptDate = new DateTime(2026, 1, 10),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập 10 cái",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-UPD",
                    ItemName = "Vật tư sửa",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 10,
                    UnitPrice = 30_000
                }
            ]
        });

        // Xuất 8 cái ngày 20/01/2026
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-UPD-TEST",
            IssueDate = new DateTime(2026, 1, 20),
            ReceivedBy = "Kho B",
            Reason = "Xuất",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "VT-UPD",
                    ItemName = "Vật tư sửa",
                    Category = "Vật tư",
                    Unit = "Cái",
                    Quantity = 8,
                    UnitPrice = 30_000
                }
            ]
        });

        // Sửa phiếu nhập giảm từ 10 xuống 5 cái -> Không đủ 8 cái để xuất ngày 20/01
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.UpdateReceiptAsync(receipt.Id, new ReceiptInput
            {
                ReceiptNumber = "PN-UPDATE-TEST",
                ReceiptDate = new DateTime(2026, 1, 10),
                DeliveredBy = "A",
                DeliveredUnit = "B",
                ReceivedBy = "C",
                Reason = "Sửa giảm còn 5 cái",
                Lines =
                [
                    new TransactionLineInput
                    {
                        ItemCode = "VT-UPD",
                        ItemName = "Vật tư sửa",
                        Category = "Vật tư",
                        Unit = "Cái",
                        Quantity = 5,
                        UnitPrice = 30_000
                    }
                ]
            }));

        Assert.Contains("không đủ tồn", ex.Message);
    }

    [Fact]
    public async Task UpsertOpeningBalances_Blocked_If_Reduced_Opening_Causes_Negative_Stock()
    {
        await using var database = await TestDatabase.CreateAsync();

        // 1. Khai báo tồn đầu năm 2026 là 20 cái
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Vật tư tồn đầu",
                Category = "Thuốc",
                Unit = "Hộp",
                UnitPrice = 10_000,
                OpeningQuantity = 20
            }
        ]);

        var item = (await database.Service.GetDashboardRowsAsync(2026)).Single();

        // 2. Xuất 15 cái ngày 10/02/2026
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-OPEN-TEST",
            IssueDate = new DateTime(2026, 2, 10),
            ReceivedBy = "Ban Điều trị",
            Reason = "Xuất",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Category = item.Category,
                    Unit = item.Unit,
                    Quantity = 15,
                    UnitPrice = item.UnitPrice
                }
            ]
        });

        // 3. Sửa tồn đầu năm 2026 giảm xuống còn 10 cái -> Thiếu 5 cái cho phiếu xuất ngày 10/02
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.UpsertOpeningBalancesAsync(2026,
            [
                new OpeningBalanceInput
                {
                    ItemName = "Vật tư tồn đầu",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    UnitPrice = 10_000,
                    OpeningQuantity = 10
                }
            ]));

        Assert.Contains("không đủ tồn tại ngày 10/02/2026", ex.Message);
    }

    [Fact]
    public async Task UpsertOpeningBalances_Rebuilds_StockLedger_BalanceAfter()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Khai báo tồn đầu 20 cái
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Vật tư sổ cái",
                Category = "Dụng cụ",
                Unit = "Bộ",
                UnitPrice = 100_000,
                OpeningQuantity = 20
            }
        ]);

        var item = (await database.Service.GetDashboardRowsAsync(2026)).Single();

        // Xuất 5 cái ngày 10/02/2026 -> BalanceAfter ban đầu trong sổ cái phải là 15
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-LEDGER-1",
            IssueDate = new DateTime(2026, 2, 10),
            ReceivedBy = "Kho 1",
            Reason = "Xuất",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Category = item.Category,
                    Unit = item.Unit,
                    Quantity = 5,
                    UnitPrice = item.UnitPrice
                }
            ]
        });

        await using (var ctx = database.CreateContext())
        {
            var initialLedger = await ctx.StockLedgerEntries.SingleAsync(x => x.DocumentNumber == "PX-LEDGER-1");
            Assert.Equal(15, initialLedger.BalanceAfter);
        }

        // Cập nhật tăng tồn đầu lên 30 cái
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Vật tư sổ cái",
                Category = "Dụng cụ",
                Unit = "Bộ",
                UnitPrice = 100_000,
                OpeningQuantity = 30
            }
        ]);

        // Kiểm tra BalanceAfter trong sổ cái tự động cập nhật lại thành 30 - 5 = 25!
        await using (var ctx = database.CreateContext())
        {
            var updatedLedger = await ctx.StockLedgerEntries.SingleAsync(x => x.DocumentNumber == "PX-LEDGER-1");
            Assert.Equal(25, updatedLedger.BalanceAfter);
        }
    }

    [Fact]
    public async Task CarryForward_With_Existing_Target_Activity_Updates_Opening_And_Rebuilds_Ledger()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Năm 2025: nhập 50 cái, xuất 20 cái -> tồn cuối 2025 = 30
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-2025-01",
            ReceiptDate = new DateTime(2025, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-MULTI",
                    ItemName = "Mặt hàng liên năm",
                    Category = "Thuốc",
                    Unit = "Lọ",
                    Quantity = 50,
                    UnitPrice = 40_000
                }
            ]
        });

        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-2025-01",
            IssueDate = new DateTime(2025, 5, 1),
            ReceivedBy = "Viện A",
            Reason = "Xuất",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-MULTI",
                    ItemName = "Mặt hàng liên năm",
                    Category = "Thuốc",
                    Unit = "Lọ",
                    Quantity = 20,
                    UnitPrice = 40_000
                }
            ]
        });

        // Năm 2026: giả sử đã phát sinh giao dịch nhập 10 cái và xuất 5 cái
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-2026-01",
            ReceiptDate = new DateTime(2026, 1, 5),
            DeliveredBy = "X",
            DeliveredUnit = "Y",
            ReceivedBy = "Z",
            Reason = "Nhập mới 2026",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-MULTI",
                    ItemName = "Mặt hàng liên năm",
                    Category = "Thuốc",
                    Unit = "Lọ",
                    Quantity = 10,
                    UnitPrice = 40_000
                }
            ]
        });

        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-2026-01",
            IssueDate = new DateTime(2026, 1, 10),
            ReceivedBy = "Kho B",
            Reason = "Xuất 2026",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-MULTI",
                    ItemName = "Mặt hàng liên năm",
                    Category = "Thuốc",
                    Unit = "Lọ",
                    Quantity = 5,
                    UnitPrice = 40_000
                }
            ]
        });

        // Nếu không cho phép cập nhật khi năm đích đã có phát sinh -> Sẽ ném ngoại lệ
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.CarryForwardPeriodAsync(2025, 2026, overwriteExisting: true, allowUpdateOpeningWithExistingActivity: false));

        // Thực hiện chuyển kỳ với allowUpdateOpeningWithExistingActivity = true
        await database.Service.CarryForwardPeriodAsync(
            2025,
            2026,
            overwriteExisting: true,
            excludeZeroClosingStock: true,
            allowUpdateOpeningWithExistingActivity: true);

        // Kiểm tra số liệu Dashboard năm 2026:
        // - OpeningQuantity = 30 (tồn cuối 2025)
        // - ReceivedQuantity = 10 (không bị reset)
        // - IssuedQuantity = 5 (không bị reset)
        // - ClosingQuantity = 30 + 10 - 5 = 35
        var dash2026 = (await database.Service.GetDashboardRowsAsync(2026)).Single();
        Assert.Equal(30, dash2026.OpeningQuantity);
        Assert.Equal(10, dash2026.ReceivedQuantity);
        Assert.Equal(5, dash2026.IssuedQuantity);
        Assert.Equal(35, dash2026.ClosingQuantity);

        // Kiểm tra sổ cái năm 2026:
        // - PN-2026-01: BalanceAfter = 30 + 10 = 40
        // - PX-2026-01: BalanceAfter = 40 - 5 = 35
        await using var ctx = database.CreateContext();
        var ledgerIn = await ctx.StockLedgerEntries.SingleAsync(x => x.DocumentNumber == "PN-2026-01");
        var ledgerOut = await ctx.StockLedgerEntries.SingleAsync(x => x.DocumentNumber == "PX-2026-01");
        Assert.Equal(40, ledgerIn.BalanceAfter);
        Assert.Equal(35, ledgerOut.BalanceAfter);
    }

    [Fact]
    public async Task CarryForward_Blocked_If_New_Opening_Causes_Negative_Stock_In_Target_Year()
    {
        await using var database = await TestDatabase.CreateAsync();

        // Năm 2025: tồn cuối chỉ có 2 cái
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-2025-LOW",
            ReceiptDate = new DateTime(2025, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-LOW",
                    ItemName = "Mặt hàng ít tồn",
                    Category = "Thuốc",
                    Unit = "Lọ",
                    Quantity = 2,
                    UnitPrice = 10_000
                }
            ]
        });

        // Năm 2026: trước đó có tồn đầu tạm là 10 cái và đã xuất 5 cái ngày 15/01/2026
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                ItemName = "Mặt hàng ít tồn",
                Category = "Thuốc",
                Unit = "Lọ",
                UnitPrice = 10_000,
                OpeningQuantity = 10
            }
        ]);

        var item = (await database.Service.GetDashboardRowsAsync(2026)).Single();
        await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "PX-2026-TEST",
            IssueDate = new DateTime(2026, 1, 15),
            ReceivedBy = "Kho B",
            Reason = "Xuất 5 cái",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Category = item.Category,
                    Unit = item.Unit,
                    Quantity = 5,
                    UnitPrice = item.UnitPrice
                }
            ]
        });

        // Khi đồng bộ tồn cuối năm 2025 (2 cái) sang làm tồn đầu 2026:
        // Năm 2026 chỉ có 2 cái mà xuất 5 cái -> Bị chặn với thông báo lỗi rõ ràng!
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.CarryForwardPeriodAsync(
                2025,
                2026,
                overwriteExisting: true,
                allowUpdateOpeningWithExistingActivity: true));

        Assert.Contains("không đủ tồn tại ngày 15/01/2026", ex.Message);
    }

    [Fact]
    public async Task Dashboard_Prefers_StockPeriod_UnitPrice_Over_Item_UnitPrice()
    {
        await using var database = await TestDatabase.CreateAsync();

        // 1. Tạo mặt hàng ban đầu với 10 cái qua phiếu nhập năm 2026
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-PRICE-1",
            ReceiptDate = new DateTime(2026, 2, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Nhập 2026",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-PRICE",
                    ItemName = "Hàng đổi đơn giá kỳ",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    Quantity = 10,
                    UnitPrice = 10_000
                }
            ]
        });

        // 2. Giả sử Catalog Item có UnitPrice = 10_000, nhưng đơn giá kỳ tồn năm 2026 được điều chỉnh là 15_000
        await using (var ctx = database.CreateContext())
        {
            var item = await ctx.Items.SingleAsync(x => x.ItemName == "Hàng đổi đơn giá kỳ");
            item.UnitPrice = 10_000;

            var period = await ctx.StockPeriods.SingleAsync(x => x.Year == 2026 && x.ItemId == item.Id);
            period.UnitPrice = 15_000;
            await ctx.SaveChangesAsync();
        }

        // 3. Dashboard năm 2026 phải ưu tiên đơn giá kỳ tồn của năm 2026 (15_000) thay vì giá gốc của Item (10_000)
        var dash2026 = (await database.Service.GetDashboardRowsAsync(2026)).Single();
        Assert.Equal(15_000, dash2026.UnitPrice);
        Assert.Equal(150_000, dash2026.Amount); // 10 * 15_000 = 150_000
    }
}
