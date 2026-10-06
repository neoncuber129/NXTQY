using Inventory.Domain.Models;
using Xunit;

namespace Inventory.Tests;

public class ItemVariantSaveTests
{
    [Fact]
    public async Task CreateReceipt_ReusesItem_WhenCategoryCasingDiffers()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddItemAsync(new Item
        {
            ItemCode = "Giay A4:10000",
            ItemName = "Giay A4",
            Category = "Van Phong",
            Unit = "Ram",
            UsageType = "",
            UnitPrice = 10000
        });

        // Cùng tên/giá/loại, danh mục khác hoa thường → phải tái sử dụng, không UNIQUE fail.
        var receipt = await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-CASE-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Giay A4",
                    Category = "van phong",
                    Unit = "Ram",
                    UsageType = "",
                    Quantity = 3,
                    UnitPrice = 10000
                }
            ]
        });

        Assert.NotNull(receipt);
        var items = await database.Service.GetItemsAsync();
        Assert.Single(items, x => x.ItemName == "Giay A4" && x.UnitPrice == 10000);
        Assert.Equal("Van Phong", items.Single(x => x.ItemName == "Giay A4").Category);
    }

    [Fact]
    public async Task CreateReceipt_CreatesSecondVariant_WhenUsageDiffers()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddItemAsync(new Item
        {
            ItemCode = "Giay A4:10000",
            ItemName = "Giay A4",
            Category = "VP",
            Unit = "Ram",
            UsageType = "trên cấp",
            UnitPrice = 10000
        });

        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-VAR-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Giay A4",
                    Category = "VP",
                    Unit = "Ram",
                    UsageType = "",
                    Quantity = 2,
                    UnitPrice = 10000
                }
            ]
        });

        var items = await database.Service.GetItemsAsync();
        Assert.Equal(2, items.Count(x => x.ItemName == "Giay A4" && x.UnitPrice == 10000));
        Assert.Contains(items, x => x.UsageType == "trên cấp" && x.ItemCode == "Giay A4:10000");
        var second = items.Single(x => x.UsageType == string.Empty);
        Assert.Equal("Giay A4:10000|VP", second.ItemCode);
    }

    [Fact]
    public async Task CreateReceipt_FirstItemKeepsBaseCode_EvenWithCategoryAndTc()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-BASE-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Giay A4",
                    Category = "VP",
                    Unit = "Ram",
                    UsageType = "trên cấp",
                    Quantity = 1,
                    UnitPrice = 10000
                }
            ]
        });

        var item = Assert.Single(await database.Service.GetItemsAsync());
        Assert.Equal("Giay A4:10000", item.ItemCode);
    }

    [Fact]
    public async Task CreateReceipt_SecondVariant_UsesTcTag()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddItemAsync(new Item
        {
            ItemCode = "Giay A4:10000",
            ItemName = "Giay A4",
            Category = "VP",
            Unit = "Ram",
            UsageType = "",
            UnitPrice = 10000
        });

        await database.Service.CreateReceiptAsync(new ReceiptInput
        {
            ReceiptNumber = "PN-TC-1",
            ReceiptDate = new DateTime(2026, 3, 1),
            DeliveredBy = "A",
            DeliveredUnit = "B",
            ReceivedBy = "C",
            Reason = "Test",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemName = "Giay A4",
                    Category = "VP",
                    Unit = "Ram",
                    UsageType = "trên cấp",
                    Quantity = 1,
                    UnitPrice = 10000
                }
            ]
        });

        var tc = (await database.Service.GetItemsAsync()).Single(x => x.UsageType == "trên cấp");
        Assert.Equal("Giay A4:10000|VP|TC", tc.ItemCode);
    }
}
