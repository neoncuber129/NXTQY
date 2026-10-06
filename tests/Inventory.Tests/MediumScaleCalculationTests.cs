using Inventory.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Inventory.Tests;

/// <summary>
/// Tính toán qua API service (không seed SQL tay) — quy mô vừa để bắt lỗi công thức nhập/xuất/phân bổ.
/// </summary>
public sealed class MediumScaleCalculationTests(ITestOutputHelper output)
{
    private const int ItemCount = 300;
    private const int Year = 2026;

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Service_api_receipt_issue_allocation_math_stays_exact()
    {
        await using var database = await TestDatabase.CreateAsync();

        var unitA = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "U-A",
            Name = "Đơn vị A",
            SortOrder = 1
        });
        var unitB = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "U-B",
            Name = "Đơn vị B",
            SortOrder = 2
        });

        // Tồn đầu: 50 mỗi mặt hàng.
        var openingLines = Enumerable.Range(1, ItemCount)
            .Select(i => new OpeningBalanceInput
            {
                Category = "Calc",
                ItemName = $"Hang tinh toan {i:D4}",
                Unit = "Hộp",
                UnitPrice = 1_000 + i % 40,
                UsageType = i % 5 == 0 ? "trên cấp" : "",
                OpeningQuantity = 50,
                Note = i % 17 == 0 ? $"Note-{i}" : null
            })
            .ToList();
        await database.Service.UpsertOpeningBalancesAsync(Year, openingLines);

        var items = await database.Service.GetItemsAsync();
        Assert.Equal(ItemCount, items.Count);
        var byName = items.ToDictionary(x => x.ItemName, StringComparer.Ordinal);

        // Mỗi mặt hàng: nhập 20, xuất 12 (phân bổ 5 + 7).
        for (var i = 1; i <= ItemCount; i++)
        {
            var name = $"Hang tinh toan {i:D4}";
            var item = byName[name];
            var price = item.UnitPrice;

            await database.Service.CreateReceiptAsync(new ReceiptInput
            {
                ReceiptNumber = $"PN-CALC-{i:D4}",
                ReceiptDate = new DateTime(Year, 2, 1 + i % 27),
                DeliveredBy = "NCC",
                DeliveredUnit = "HD",
                ReceivedBy = "Kho",
                Reason = "Nhap hang loat",
                Lines =
                [
                    new TransactionLineInput
                    {
                        ItemCode = item.ItemCode,
                        ItemName = item.ItemName,
                        Category = item.Category,
                        Unit = item.Unit,
                        UsageType = item.UsageType,
                        Quantity = 20,
                        UnitPrice = price,
                        Note = i % 11 == 0 ? $"PN-{i}" : null
                    }
                ]
            });

            await database.Service.CreateIssueAsync(new IssueInput
            {
                IssueNumber = $"CD-CALC-{i:D4}",
                IssueDate = new DateTime(Year, 3, 1 + i % 27),
                ReceivedBy = "Don vi",
                Reason = "Xuat can doi",
                Lines =
                [
                    new TransactionLineInput
                    {
                        ItemCode = item.ItemCode,
                        ItemName = item.ItemName,
                        Category = item.Category,
                        Unit = item.Unit,
                        UsageType = item.UsageType,
                        Quantity = 12,
                        UnitPrice = price,
                        Allocations = new Dictionary<int, decimal>
                        {
                            [unitA.Id] = 5,
                            [unitB.Id] = 7
                        }
                    }
                ]
            });
        }

        var dashboard = await database.Service.GetDashboardRowsAsync(Year);
        Assert.Equal(ItemCount, dashboard.Count);

        // 50 mở đầu + 20 nhập − 12 xuất = 58.
        Assert.All(dashboard, row =>
        {
            Assert.Equal(50, row.OpeningQuantity);
            Assert.Equal(20, row.ReceivedQuantity);
            Assert.Equal(12, row.IssuedQuantity);
            Assert.Equal(58, row.ClosingQuantity);
            Assert.Equal(row.ClosingQuantity * row.UnitPrice, row.Amount);
        });

        var expectedClosing = ItemCount * 58m;
        Assert.Equal(expectedClosing, dashboard.Sum(x => x.ClosingQuantity));
        Assert.Equal(
            dashboard.Sum(x => x.ClosingQuantity * x.UnitPrice),
            dashboard.Sum(x => x.Amount));

        var stock = await database.Service.GetAvailableStockBatchAsync(
            dashboard.Select(x => x.ItemCode).ToList(),
            new DateTime(Year, 12, 31));
        Assert.Equal(expectedClosing, stock.Values.Sum());
        foreach (var row in dashboard)
        {
            Assert.Equal(58, stock[row.ItemCode]);
        }

        await using var db = database.CreateContext();
        var children = await db.Issues.AsNoTracking()
            .Where(x => x.ParentIssueId != null)
            .CountAsync();
        Assert.Equal(ItemCount * 2, children);

        var allocationQtys = await db.IssueLineAllocations.AsNoTracking()
            .Select(x => x.Quantity)
            .ToListAsync();
        Assert.Equal(ItemCount * 12m, allocationQtys.Sum());

        // Phiếu con không ghi sổ (chỉ phiếu cân đối cha).
        var childNumbers = await db.Issues.AsNoTracking()
            .Where(x => x.ParentIssueId != null)
            .Select(x => x.IssueNumber)
            .ToListAsync();
        Assert.Equal(
            0,
            await db.StockLedgerEntries.CountAsync(x => childNumbers.Contains(x.DocumentNumber)));

        var activity = await database.Service.GetYearStockActivityAsync(Year);
        Assert.Equal(ItemCount, activity.ReceiptVoucherCount);
        Assert.Equal(ItemCount, activity.BalanceIssueVoucherCount);
        Assert.Equal(ItemCount * 20m, activity.TotalReceived);
        Assert.Equal(ItemCount * 12m, activity.TotalIssued);

        // Sửa một phiếu nhập: giảm SL → tồn đổi đúng.
        var sample = byName["Hang tinh toan 0001"];
        var receipt = (await database.Service.GetReceiptsAsync(Year))
            .Single(x => x.ReceiptNumber == "PN-CALC-0001");
        await database.Service.UpdateReceiptAsync(receipt.Id, new ReceiptInput
        {
            ReceiptNumber = receipt.ReceiptNumber,
            ReceiptDate = receipt.ReceiptDate,
            DeliveredBy = receipt.DeliveredBy,
            DeliveredUnit = receipt.DeliveredUnit,
            ReceivedBy = receipt.ReceivedBy,
            Reason = receipt.Reason,
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = sample.ItemCode,
                    ItemName = sample.ItemName,
                    Category = sample.Category,
                    Unit = sample.Unit,
                    UsageType = sample.UsageType,
                    Quantity = 15,
                    UnitPrice = sample.UnitPrice
                }
            ]
        });

        var updatedRow = (await database.Service.GetDashboardRowsAsync(Year))
            .Single(x => x.ItemCode == sample.ItemCode);
        Assert.Equal(15, updatedRow.ReceivedQuantity);
        Assert.Equal(50 + 15 - 12, updatedRow.ClosingQuantity); // 53
        Assert.Equal(
            53,
            await database.Service.GetAvailableStockAsync(sample.ItemCode, new DateTime(Year, 12, 31)));

        output.WriteLine(
            $"Medium scale OK: {ItemCount} items, closing total={expectedClosing - 5} after one receipt edit.");
    }
}
