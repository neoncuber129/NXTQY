using Inventory.Domain.Models;
using Xunit;

namespace Inventory.Tests;

public class OrphanItemCleanupTests
{
    [Fact]
    public async Task DeleteReceipt_RemovesItemCreatedOnlyByThatReceipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-ORPHAN-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Hang chi tren phieu nay",
                    Category = "Cat",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 5,
                    UnitPrice = 1000
                }
            ]
        });

        var itemsBefore = await database.Service.GetItemsAsync();
        Assert.Contains(itemsBefore, x => x.ItemName == "Hang chi tren phieu nay");

        await database.Service.DeleteReceiptAsync(receipt.Id);

        var itemsAfter = await database.Service.GetItemsAsync();
        Assert.DoesNotContain(itemsAfter, x => x.ItemName == "Hang chi tren phieu nay");
        Assert.Empty(await database.Service.GetReceiptsAsync(2026));
    }

    [Fact]
    public async Task DeleteReceipt_KeepsItemStillUsedElsewhere()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-KEEP-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Hang dung chung",
                    Category = "Cat",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 5,
                    UnitPrice = 1000
                }
            ]
        });

        var itemCode = (await database.Service.GetItemsAsync())
            .Single(x => x.ItemName == "Hang dung chung").ItemCode;

        var second = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-KEEP-2",
            ReceiptDate = new DateTime(2026, 3, 2),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = itemCode,
                    ItemName = "Hang dung chung",
                    Category = "Cat",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 2,
                    UnitPrice = 1000
                }
            ]
        });

        await database.Service.DeleteReceiptAsync(second.Id);

        var items = await database.Service.GetItemsAsync();
        Assert.Contains(items, x => x.ItemCode == itemCode);
        Assert.NotNull(first);
    }
}
