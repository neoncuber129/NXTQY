using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Inventory.Tests;

public sealed class LargeLoadTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Ten_thousand_items_and_fifty_thousand_vouchers_remain_consistent()
    {
        await using var database = await TestDatabase.CreateAsync();
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);

        var seedWatch = Stopwatch.StartNew();
        var seed = LargeDataSeeder.Seed(database.DatabasePath);
        seedWatch.Stop();
        output.WriteLine($"Seed: {seedWatch.Elapsed}; memory baseline: {baselineMemory:N0} bytes");

        var dashboardWatch = Stopwatch.StartNew();
        var dashboard = await database.Service.GetDashboardRowsAsync(seed.Year);
        dashboardWatch.Stop();
        Assert.Equal(seed.ItemCount, dashboard.Count);
        Assert.Equal(seed.ExpectedTotalClosing, dashboard.Sum(x => x.ClosingQuantity));
        Assert.All(dashboard, row =>
        {
            Assert.Equal(
                row.OpeningQuantity + row.ReceivedQuantity - row.IssuedQuantity,
                row.ClosingQuantity);
            Assert.Equal(row.ClosingQuantity * row.UnitPrice, row.Amount);
        });
        var expectedDashboardAmount = dashboard.Sum(x => x.ClosingQuantity * x.UnitPrice);
        Assert.Equal(expectedDashboardAmount, dashboard.Sum(x => x.Amount));
        output.WriteLine(
            $"Dashboard 10k rows: {dashboardWatch.Elapsed}; total amount={dashboard.Sum(x => x.Amount):N0}");

        await using (var mathContext = database.CreateContext())
        {
            // Nhập/xuất trên kỳ phải khớp tổng dòng phiếu (nguồn tính tồn).
            // SQLite không SUM decimal ổn định trong GroupBy → cộng phía client.
            var receiptLines = await mathContext.ReceiptLines.AsNoTracking()
                .Select(x => new { x.ItemId, x.Quantity, x.Amount, x.UnitPrice })
                .ToListAsync();
            var issueLines = await mathContext.IssueLines.AsNoTracking()
                .Select(x => new { x.ItemId, x.Quantity, x.Amount, x.UnitPrice })
                .ToListAsync();
            var receivedByLines = receiptLines
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => (Qty: g.Sum(x => x.Quantity), Amount: g.Sum(x => x.Amount)));
            var issuedByLines = issueLines
                .GroupBy(x => x.ItemId)
                .ToDictionary(g => g.Key, g => (Qty: g.Sum(x => x.Quantity), Amount: g.Sum(x => x.Amount)));
            var periods = await mathContext.StockPeriods.AsNoTracking()
                .Where(x => x.Year == seed.Year)
                .ToListAsync();

            Assert.Equal(seed.ItemCount, periods.Count);
            foreach (var period in periods)
            {
                receivedByLines.TryGetValue(period.ItemId, out var received);
                issuedByLines.TryGetValue(period.ItemId, out var issued);
                Assert.Equal(received.Qty, period.ReceivedQuantity);
                Assert.Equal(issued.Qty, period.IssuedQuantity);
                Assert.Equal(
                    period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity,
                    period.ClosingQuantity);
            }

            // Tổng tiền phiếu = tổng thành tiền dòng.
            var receiptTotals = await mathContext.Receipts.AsNoTracking()
                .Select(x => x.TotalAmount)
                .ToListAsync();
            Assert.Equal(receiptLines.Sum(x => x.Amount), receiptTotals.Sum());

            var issueHeaders = await mathContext.Issues.AsNoTracking()
                .Where(x => x.ParentIssueId == null)
                .Select(x => x.TotalAmount)
                .ToListAsync();
            Assert.Equal(issueLines.Sum(x => x.Amount), issueHeaders.Sum());

            // Thành tiền dòng = SL × đơn giá.
            Assert.All(receiptLines, x => Assert.Equal(x.Quantity * x.UnitPrice, x.Amount));
            Assert.All(issueLines, x => Assert.Equal(x.Quantity * x.UnitPrice, x.Amount));
        }

        var receiptWatch = Stopwatch.StartNew();
        var receipts = await database.Service.GetReceiptsAsync(seed.Year);
        receiptWatch.Stop();
        Assert.Equal(seed.ReceiptCount, receipts.Count);
        Assert.Equal(
            await SumReceiptLineAmountsAsync(database),
            receipts.Sum(x => x.TotalAmount));
        output.WriteLine($"Receipts 25k headers: {receiptWatch.Elapsed}");

        var issueWatch = Stopwatch.StartNew();
        var issues = await database.Service.GetIssuesAsync(seed.Year);
        issueWatch.Stop();
        Assert.Equal(seed.IssueCount, issues.Count);
        Assert.Equal(
            await SumIssueLineAmountsAsync(database),
            issues.Sum(x => x.TotalAmount));
        output.WriteLine($"Issues 25k headers: {issueWatch.Elapsed}");

        await using (var countContext = database.CreateContext())
        {
            Assert.Equal(seed.ReceiptCount, await countContext.ReceiptLines.CountAsync());
            Assert.Equal(seed.IssueCount, await countContext.IssueLines.CountAsync());
        }

        var codes = dashboard.Select(x => x.ItemCode).ToList();
        var stockWatch = Stopwatch.StartNew();
        var stock = await database.Service.GetAvailableStockBatchAsync(
            codes,
            new DateTime(seed.Year, 12, 31));
        stockWatch.Stop();
        Assert.Equal(seed.ItemCount, stock.Count);
        Assert.Equal(seed.ExpectedTotalClosing, stock.Values.Sum());
        // Tồn batch phải khớp cột còn lại trên Dashboard từng mã.
        foreach (var row in dashboard)
        {
            Assert.Equal(row.ClosingQuantity, stock[row.ItemCode]);
        }

        output.WriteLine($"Batch stock 10k codes: {stockWatch.Elapsed}");

        var activityWatch = Stopwatch.StartNew();
        var activity = await database.Service.GetYearStockActivityAsync(seed.Year);
        activityWatch.Stop();
        Assert.Equal(seed.ItemCount, activity.PeriodCount);
        Assert.Equal(seed.ReceiptCount, activity.ReceiptVoucherCount);
        Assert.Equal(seed.IssueCount, activity.BalanceIssueVoucherCount);
        Assert.Equal(dashboard.Sum(x => x.ReceivedQuantity), activity.TotalReceived);
        Assert.Equal(dashboard.Sum(x => x.IssuedQuantity), activity.TotalIssued);
        output.WriteLine($"Year activity: {activityWatch.Elapsed}");

        var carryWatch = Stopwatch.StartNew();
        await database.Service.CarryForwardPeriodAsync(seed.Year, seed.Year + 1, overwriteExisting: false);
        carryWatch.Stop();
        await using var context = database.CreateContext();
        var nextPeriods = await context.StockPeriods.AsNoTracking()
            .Where(x => x.Year == seed.Year + 1)
            .ToListAsync();
        Assert.Equal(seed.ItemCount, nextPeriods.Count);
        Assert.Equal(seed.ExpectedTotalClosing, nextPeriods.Sum(x => x.OpeningQuantity));
        Assert.All(nextPeriods, period =>
        {
            Assert.Equal(0, period.ReceivedQuantity);
            Assert.Equal(0, period.IssuedQuantity);
            Assert.Equal(period.OpeningQuantity, period.ClosingQuantity);
        });

        var nextDashboard = await database.Service.GetDashboardRowsAsync(seed.Year + 1);
        Assert.Equal(seed.ExpectedTotalClosing, nextDashboard.Sum(x => x.OpeningQuantity));
        Assert.Equal(seed.ExpectedTotalClosing, nextDashboard.Sum(x => x.ClosingQuantity));
        Assert.All(nextDashboard, row => Assert.Equal(0, row.ReceivedQuantity + row.IssuedQuantity));
        output.WriteLine($"Carry-forward 10k periods: {carryWatch.Elapsed}");

        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        output.WriteLine(
            $"Final managed memory: {finalMemory:N0} bytes; delta: {finalMemory - baselineMemory:N0} bytes");
    }

    private static async Task<decimal> SumReceiptLineAmountsAsync(TestDatabase database)
    {
        await using var db = database.CreateContext();
        var amounts = await db.ReceiptLines.AsNoTracking().Select(x => x.Amount).ToListAsync();
        return amounts.Sum();
    }

    private static async Task<decimal> SumIssueLineAmountsAsync(TestDatabase database)
    {
        await using var db = database.CreateContext();
        var amounts = await (
            from line in db.IssueLines.AsNoTracking()
            join issue in db.Issues.AsNoTracking() on line.IssueId equals issue.Id
            where issue.ParentIssueId == null
            select line.Amount).ToListAsync();
        return amounts.Sum();
    }
}
