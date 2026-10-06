using Inventory.Domain.Models;
using Xunit;

namespace Inventory.Tests;

public class ReceiptNoteDashboardTests
{
    [Fact]
    public async Task CreateReceipt_WithLineNote_AppearsOnDashboard()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-NOTE-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Hang co ghi chu",
                    Category = "VP",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 2,
                    UnitPrice = 5000,
                    Note = "Ghi chu tu phieu nhap"
                }
            ]
        });

        var row = Assert.Single(
            await database.Service.GetDashboardRowsAsync(2026),
            x => x.ItemName == "Hang co ghi chu");
        Assert.Equal("Ghi chu tu phieu nhap", row.Note);
    }

    [Fact]
    public async Task UpdateReceipt_LineNote_UpdatesDashboardNote()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-NOTE-2",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Hang update note",
                    Category = "VP",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 1,
                    UnitPrice = 1000,
                    Note = "Cu"
                }
            ]
        });

        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.UpdateReceiptAsync(receipt.Id, new ReceiptInput
        {
            ReceiptNumber = "PN-NOTE-2",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = item.ItemCode,
                    ItemName = item.ItemName,
                    Category = item.Category,
                    Unit = item.Unit,
                    UsageType = item.UsageType,
                    Quantity = 1,
                    UnitPrice = 1000,
                    Note = "Moi tu sua phieu"
                }
            ]
        });

        var row = Assert.Single(await database.Service.GetDashboardRowsAsync(2026));
        Assert.Equal("Moi tu sua phieu", row.Note);
    }

    [Fact]
    public async Task UpdateReceipt_ClearNotes_ClearsDashboardWhenSoleSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receipt = await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-CLR",
            new DateTime(2026, 4, 1),
            "Hang clear note",
            note: "Con note"));

        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.UpdateReceiptAsync(receipt.Id, BaseReceipt(
            "PN-CLR",
            new DateTime(2026, 4, 1),
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: null));

        var row = Assert.Single(await database.Service.GetDashboardRowsAsync(2026));
        Assert.True(string.IsNullOrWhiteSpace(row.Note));
    }

    [Fact]
    public async Task UpdateReceipt_ClearNotes_KeepsOtherReceiptNote()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-A",
            new DateTime(2026, 4, 1),
            "Hang 2 phieu",
            note: "Tu phieu A"));
        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-B",
            new DateTime(2026, 5, 1),
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: "Tu phieu B"));

        await database.Service.UpdateReceiptAsync(first.Id, BaseReceipt(
            "PN-A",
            new DateTime(2026, 4, 1),
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: null));

        var row = Assert.Single(await database.Service.GetDashboardRowsAsync(2026));
        Assert.Equal("Tu phieu B", row.Note);
    }

    [Fact]
    public async Task DeleteReceipt_ClearsDashboardNote_WhenNoOtherSource()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receipt = await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-DEL",
            new DateTime(2026, 4, 1),
            "Hang xoa phieu",
            note: "Se mat"));

        // Giữ mặt hàng bằng tồn đầu = 0 kỳ (nhận từ phiếu) — xóa phiếu có thể xóa item.
        // Tạo tồn đầu để item sống sót sau xóa phiếu.
        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.UpsertOpeningBalancesAsync(2026,
        [
            new OpeningBalanceInput
            {
                Category = item.Category,
                ItemName = item.ItemName,
                Unit = item.Unit,
                UnitPrice = item.UnitPrice,
                UsageType = item.UsageType,
                OpeningQuantity = 1,
                Note = null
            }
        ]);

        await database.Service.DeleteReceiptAsync(receipt.Id);

        var row = Assert.Single(
            await database.Service.GetDashboardRowsAsync(2026),
            x => x.ItemName == "Hang xoa phieu");
        Assert.True(string.IsNullOrWhiteSpace(row.Note));
    }

    [Fact]
    public async Task DashboardManualNote_LockedUntilLaterReceipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-LOCK-1",
            new DateTime(2026, 3, 1),
            "Hang khoa note",
            note: "Tu phieu cu"));

        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.UpdateItemsMetadataAsync(
        [
            new ItemMetadataPatch(item.Id, item.Category, item.UsageType, "Ghi chu dashboard")
        ]);

        Assert.Equal("Ghi chu dashboard", Assert.Single(await database.Service.GetDashboardRowsAsync(2026)).Note);

        // Phiếu cùng ngày hoặc trước — không ghi đè khóa Dashboard (ngày khóa = hôm nay > 1/3).
        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-LOCK-2",
            new DateTime(2026, 3, 2),
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: "Phieu som hon khoa"));

        Assert.Equal("Ghi chu dashboard", Assert.Single(await database.Service.GetDashboardRowsAsync(2026)).Note);

        // Phiếu ngày hôm nay (>= ngày khóa) → lấy ghi chú phiếu.
        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-LOCK-3",
            DateTime.Today,
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: "Phieu sau khoa"));

        Assert.Equal("Phieu sau khoa", Assert.Single(await database.Service.GetDashboardRowsAsync(2026)).Note);
    }

    [Fact]
    public async Task DashboardManualNote_LaterReceiptEmpty_ClearsNote()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-ME-1",
            new DateTime(2026, 2, 1),
            "Hang clear manual",
            note: "Cu"));
        var item = Assert.Single(await database.Service.GetItemsAsync());
        await database.Service.UpdateItemsMetadataAsync(
        [
            new ItemMetadataPatch(item.Id, item.Category, item.UsageType, "Manual")
        ]);

        await database.Service.CreateReceiptAsync(BaseReceipt(
            "PN-ME-2",
            DateTime.Today,
            item.ItemName,
            itemCode: item.ItemCode,
            category: item.Category,
            unit: item.Unit,
            note: null));

        Assert.True(string.IsNullOrWhiteSpace(Assert.Single(await database.Service.GetDashboardRowsAsync(2026)).Note));
    }

    private static ReceiptInput BaseReceipt(
        string number,
        DateTime date,
        string itemName,
        string? itemCode = null,
        string category = "VP",
        string unit = "Hộp",
        string? note = null,
        decimal qty = 1,
        decimal price = 1000) =>
        new()
        {
            ReceiptNumber = number,
            ReceiptDate = date,
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = itemCode ?? string.Empty,
                    ItemName = itemName,
                    Category = category,
                    Unit = unit,
                    UsageType = "",
                    Quantity = qty,
                    UnitPrice = price,
                    Note = note
                }
            ]
        };
}
