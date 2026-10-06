using Inventory.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public sealed class InventoryCorrectnessTests
{
    [Fact]
    public async Task Receipt_balance_and_unit_children_keep_stock_invariants()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unitA = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "A",
            Name = "Đơn vị A",
            ReceivedBy = "Người A",
            DeliveredBy = "Kho",
            Reason = "Cấp hàng"
        });
        var unitB = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "B",
            Name = "Đơn vị B",
            ReceivedBy = "Người B",
            DeliveredBy = "Kho",
            Reason = "Cấp hàng"
        });

        var receipt = await database.Service.CreateReceiptAsync(Receipt(
            "PN-001",
            new DateTime(2026, 1, 10),
            "MH001",
            100,
            12_500));
        var issue = await database.Service.CreateIssueAsync(Issue(
            "PX-001",
            new DateTime(2026, 1, 15),
            "MH001",
            30,
            12_500,
            new Dictionary<int, decimal>
            {
                [unitA.Id] = 10,
                [unitB.Id] = 20
            }));

        await using var context = database.CreateContext();
        var period = await context.StockPeriods.SingleAsync();
        Assert.Equal(100, period.ReceivedQuantity);
        Assert.Equal(30, period.IssuedQuantity);
        Assert.Equal(
            period.OpeningQuantity + period.ReceivedQuantity - period.IssuedQuantity,
            period.ClosingQuantity);

        var children = await database.Service.GetGeneratedUnitIssuesAsync(issue.Id);
        Assert.Equal(2, children.Count);
        Assert.Equal(30, children.Sum(x => x.Lines.Sum(l => l.Quantity)));
        Assert.Equal(["A", "B"], children.Select(x => x.ReceivedUnitCode).Order().ToArray());

        var childNumbers = children.Select(x => x.IssueNumber).ToList();
        var childLedgerCount = await context.StockLedgerEntries.CountAsync(x =>
            childNumbers.Contains(x.DocumentNumber));
        Assert.Equal(0, childLedgerCount);

        var snapshots = await database.Service.GetBalanceStockSnapshotsAsync(issue.Id);
        var itemId = receipt.Lines.Single().ItemId;
        Assert.Equal(new BalanceStockSnapshot(100, 70), snapshots[itemId]);
        var actualItemCode = await context.Items
            .Where(x => x.Id == itemId)
            .Select(x => x.ItemCode)
            .SingleAsync();
        Assert.Equal(70, await database.Service.GetAvailableStockAsync(actualItemCode, new DateTime(2026, 12, 31)));
    }

    [Fact]
    public async Task New_unit_issue_uses_reason_from_balance_instead_of_unit_defaults()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unit = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "REASON",
            Name = "Đơn vị lý do",
            Reason = "Lý do cũ tại đơn vị"
        });
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-REASON",
            new DateTime(2026, 1, 1),
            "MH-REASON",
            10,
            1_000));
        var input = Issue(
            "CD-REASON",
            new DateTime(2026, 1, 2),
            "MH-REASON",
            4,
            1_000,
            new Dictionary<int, decimal> { [unit.Id] = 4 });
        input.Reason = "Lý do nhập tại phiếu cân đối";

        var balance = await database.Service.CreateIssueAsync(input);
        var child = Assert.Single(await database.Service.GetGeneratedUnitIssuesAsync(balance.Id));

        Assert.Equal(input.Reason, balance.Reason);
        Assert.Equal(input.Reason, child.Reason);
    }

    [Fact]
    public async Task Backdated_documents_rebuild_chronological_ledger_balances()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-LATE",
            new DateTime(2026, 1, 20),
            "MH001",
            10,
            1_000));
        await database.Service.CreateIssueAsync(Issue(
            "PX-LATE",
            new DateTime(2026, 1, 25),
            "MH001",
            5,
            1_000));
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-EARLY",
            new DateTime(2026, 1, 10),
            "MH001",
            20,
            1_000));

        await using var context = database.CreateContext();
        var balances = await context.StockLedgerEntries
            .OrderBy(x => x.TransactionDate)
            .ThenBy(x => x.Id)
            .Select(x => x.BalanceAfter)
            .ToListAsync();
        Assert.Equal([20m, 30m, 25m], balances);
    }

    [Fact]
    public async Task Service_rejects_issue_that_exceeds_available_stock()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-STOCK",
            new DateTime(2026, 2, 1),
            "MH001",
            5,
            1_000));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.CreateIssueAsync(Issue(
                "PX-OVER",
                new DateTime(2026, 2, 2),
                "MH001",
                6,
                1_000)));
        Assert.Contains("tồn", exception.Message, StringComparison.OrdinalIgnoreCase);

        await using var context = database.CreateContext();
        Assert.Empty(await context.Issues.ToListAsync());
        Assert.Equal(5, (await context.StockPeriods.SingleAsync()).ClosingQuantity);
    }

    [Fact]
    public async Task Batch_stock_lookup_is_case_insensitive_and_matches_scalar()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.AddItemAsync(new Item
        {
            ItemCode = "AbC-01",
            ItemName = "Tên AbC-01",
            Category = "Thuốc",
            Unit = "Hộp",
            UsageType = "Cấp trên",
            UnitPrice = 1_000
        });
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-CASE",
            new DateTime(2026, 3, 1),
            "AbC-01",
            9,
            1_000));

        var batch = await database.Service.GetAvailableStockBatchAsync(
            ["abc-01", " ABC-01 ", "missing"],
            new DateTime(2026, 3, 2));

        Assert.Equal(9, batch["abc-01"]);
        Assert.Equal(0, batch["missing"]);
        Assert.Equal(
            batch["abc-01"],
            await database.Service.GetAvailableStockAsync("ABC-01", new DateTime(2026, 3, 2)));
    }

    [Fact]
    public async Task Updating_allocations_preserves_edited_child_header_and_removes_zero_unit()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unitA = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "A",
            Name = "Đơn vị A"
        });
        var unitB = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "B",
            Name = "Đơn vị B"
        });
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-ALLOC",
            new DateTime(2026, 4, 1),
            "MH001",
            100,
            1_000));
        var issue = await database.Service.CreateIssueAsync(Issue(
            "PX-ALLOC",
            new DateTime(2026, 4, 2),
            "MH001",
            30,
            1_000,
            new Dictionary<int, decimal> { [unitA.Id] = 10, [unitB.Id] = 20 }));

        var children = await database.Service.GetGeneratedUnitIssuesAsync(issue.Id);
        var childA = children.Single(x => x.SubordinateUnitId == unitA.Id);
        await database.Service.UpdateUnitIssueHeadersAsync(issue.Id,
        [
            new UnitIssueHeaderPatch
            {
                IssueId = childA.Id,
                IssueNumber = "PX-A-CUSTOM",
                IssueDate = childA.IssueDate,
                ReceivedBy = "Đã sửa",
                DeliveredBy = childA.DeliveredBy,
                Reason = childA.Reason
            },
            .. children.Where(x => x.Id != childA.Id).Select(x => new UnitIssueHeaderPatch
            {
                IssueId = x.Id,
                IssueNumber = x.IssueNumber,
                IssueDate = x.IssueDate,
                ReceivedBy = x.ReceivedBy,
                DeliveredBy = x.DeliveredBy,
                Reason = x.Reason
            })
        ]);

        await database.Service.UpdateIssueAsync(issue.Id, Issue(
            "PX-ALLOC",
            new DateTime(2026, 4, 2),
            "MH001",
            30,
            1_000,
            new Dictionary<int, decimal> { [unitA.Id] = 30 }));

        var updatedChildren = await database.Service.GetGeneratedUnitIssuesAsync(issue.Id);
        var onlyChild = Assert.Single(updatedChildren);
        Assert.Equal(unitA.Id, onlyChild.SubordinateUnitId);
        Assert.Equal("PX-A-CUSTOM", onlyChild.IssueNumber);
        Assert.Equal("Đã sửa", onlyChild.ReceivedBy);
        Assert.Equal(30, onlyChild.Lines.Single().Quantity);
    }

    [Fact]
    public async Task Reconciliation_is_idempotent_and_child_issues_never_change_stock()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-REC",
            new DateTime(2026, 5, 1),
            "MH001",
            20,
            1_000));
        await database.Service.CreateIssueAsync(Issue(
            "PX-REC",
            new DateTime(2026, 5, 2),
            "MH001",
            7,
            1_000));

        await database.Service.ReconcileIssuedStockFromBalanceIssuesAsync();
        await using var firstContext = database.CreateContext();
        var firstPeriod = await firstContext.StockPeriods.AsNoTracking().SingleAsync();
        var firstLedger = await firstContext.StockLedgerEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new { x.TransactionType, x.DocumentNumber, x.BalanceAfter })
            .ToListAsync();

        await database.Service.ReconcileIssuedStockFromBalanceIssuesAsync();
        await using var secondContext = database.CreateContext();
        var secondPeriod = await secondContext.StockPeriods.AsNoTracking().SingleAsync();
        var secondLedger = await secondContext.StockLedgerEntries.AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new { x.TransactionType, x.DocumentNumber, x.BalanceAfter })
            .ToListAsync();

        Assert.Equal(firstPeriod.IssuedQuantity, secondPeriod.IssuedQuantity);
        Assert.Equal(firstPeriod.ClosingQuantity, secondPeriod.ClosingQuantity);
        Assert.Equal(firstLedger, secondLedger);
    }

    [Fact]
    public async Task Concurrent_issues_cannot_oversell_the_same_item()
    {
        await using var database = await TestDatabase.CreateAsync();
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-CONCURRENT",
            new DateTime(2026, 6, 1),
            "MH001",
            10,
            1_000));

        async Task<bool> TryCreateAsync(string number)
        {
            try
            {
                await database.Service.CreateIssueAsync(Issue(
                    number,
                    new DateTime(2026, 6, 2),
                    "MH001",
                    7,
                    1_000));
                return true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or DbUpdateException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(
            TryCreateAsync("PX-CONCURRENT-1"),
            TryCreateAsync("PX-CONCURRENT-2"));
        Assert.Single(results, x => x);

        await using var context = database.CreateContext();
        var period = await context.StockPeriods.SingleAsync();
        Assert.Equal(7, period.IssuedQuantity);
        Assert.Equal(3, period.ClosingQuantity);
        Assert.Single(await context.Issues.Where(x => x.ParentIssueId == null).ToListAsync());
    }

    [Fact]
    public async Task Backup_and_restore_include_committed_wal_data_and_remain_valid()
    {
        await using var database = await TestDatabase.CreateAsync();
        var receipt = await database.Service.CreateReceiptAsync(Receipt(
            "PN-BACKUP",
            new DateTime(2026, 7, 1),
            "MH001",
            12,
            1_000));
        var backupPath = Path.Combine(database.DirectoryPath, "backup.db");

        await database.Service.BackupDatabaseAsync(backupPath);
        await using (var backupConnection = new Microsoft.Data.Sqlite.SqliteConnection(
                         $"Data Source={backupPath};Mode=ReadOnly"))
        {
            await backupConnection.OpenAsync();
            await using var integrityCommand = backupConnection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", await integrityCommand.ExecuteScalarAsync());
            await using var countCommand = backupConnection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ReceiptNumber = 'PN-BACKUP';";
            Assert.Equal(1L, await countCommand.ExecuteScalarAsync());
        }

        await database.Service.DeleteReceiptAsync(receipt.Id);
        Assert.Empty(await database.Service.GetReceiptsAsync(2026));
        await database.Service.RestoreDatabaseAsync(backupPath);
        var restored = await database.Service.GetReceiptsAsync(2026);
        Assert.Equal("PN-BACKUP", Assert.Single(restored).ReceiptNumber);
    }

    [Fact]
    public async Task Unit_issue_headers_update_atomically_across_multiple_balance_parents()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unit = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "MULTI",
            Name = "Đơn vị nhiều phiếu",
            ReceivedBy = "Người cũ",
            Reason = "Lý do cũ"
        });
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-MULTI",
            new DateTime(2026, 9, 1),
            "MH-MULTI",
            100,
            1_000));
        var parent1 = await database.Service.CreateIssueAsync(Issue(
            "CD-MULTI-1",
            new DateTime(2026, 9, 2),
            "MH-MULTI",
            10,
            1_000,
            new Dictionary<int, decimal> { [unit.Id] = 10 }));
        var parent2 = await database.Service.CreateIssueAsync(Issue(
            "CD-MULTI-2",
            new DateTime(2026, 9, 3),
            "MH-MULTI",
            15,
            1_000,
            new Dictionary<int, decimal> { [unit.Id] = 15 }));
        var child1 = Assert.Single(await database.Service.GetGeneratedUnitIssuesAsync(parent1.Id));
        var child2 = Assert.Single(await database.Service.GetGeneratedUnitIssuesAsync(parent2.Id));

        await database.Service.UpdateUnitIssueHeadersAsync(
        [
            new UnitIssueHeaderPatch
            {
                IssueId = child1.Id,
                IssueNumber = "PX-MULTI-A",
                IssueDate = new DateTime(2026, 10, 1),
                ReceivedBy = "Người A",
                Reason = "Lý do A"
            },
            new UnitIssueHeaderPatch
            {
                IssueId = child2.Id,
                IssueNumber = "PX-MULTI-B",
                IssueDate = new DateTime(2026, 10, 2),
                ReceivedBy = "Người B",
                Reason = "Lý do B"
            }
        ]);

        var updated = await database.Service.GetUnitIssuesByIdsAsync([child1.Id, child2.Id]);
        Assert.Equal(["PX-MULTI-A", "PX-MULTI-B"], updated.Select(x => x.IssueNumber).ToArray());
        Assert.Equal(["Người A", "Người B"], updated.Select(x => x.ReceivedBy).ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Service.UpdateUnitIssueHeadersAsync(
            [
                new UnitIssueHeaderPatch
                {
                    IssueId = child1.Id,
                    IssueNumber = "PX-SHOULD-NOT-SAVE",
                    IssueDate = new DateTime(2026, 11, 1)
                },
                new UnitIssueHeaderPatch
                {
                    IssueId = child2.Id,
                    IssueNumber = parent1.IssueNumber,
                    IssueDate = new DateTime(2026, 11, 2)
                }
            ]));

        var afterFailure = await database.Service.GetUnitIssuesByIdsAsync([child1.Id, child2.Id]);
        Assert.Equal(["PX-MULTI-A", "PX-MULTI-B"], afterFailure.Select(x => x.IssueNumber).ToArray());
    }

    [Fact]
    public async Task Clear_all_data_deletes_child_issues_before_parent_without_foreign_key_error()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unit = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "CLEAR",
            Name = "Đơn vị xóa dữ liệu"
        });
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-CLEAR",
            new DateTime(2026, 8, 1),
            "MH-CLEAR",
            10,
            1_000));
        await database.Service.CreateIssueAsync(Issue(
            "CD-CLEAR",
            new DateTime(2026, 8, 2),
            "MH-CLEAR",
            4,
            1_000,
            new Dictionary<int, decimal> { [unit.Id] = 4 }));

        await database.Service.ClearAllDataAsync();

        await using var context = database.CreateContext();
        Assert.Empty(await context.Issues.ToListAsync());
        Assert.Empty(await context.IssueLines.ToListAsync());
        Assert.Empty(await context.IssueLineAllocations.ToListAsync());
        Assert.Empty(await context.Receipts.ToListAsync());
        Assert.Empty(await context.ReceiptLines.ToListAsync());
        Assert.Empty(await context.StockPeriods.ToListAsync());
        Assert.Empty(await context.StockLedgerEntries.ToListAsync());
        Assert.Empty(await context.Items.ToListAsync());
        Assert.Empty(await context.SubordinateUnits.ToListAsync());
        Assert.Empty(await context.AppMeta.ToListAsync());
    }

    [Fact]
    public async Task Unit_issue_report_filters_by_child_date_unit_keyword_and_inclusive_boundaries()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unitA = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "A01",
            Name = "Đơn vị Alpha",
            SortOrder = 1
        });
        var unitB = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "B01",
            Name = "Đơn vị Beta",
            SortOrder = 2
        });

        await using (var context = database.CreateContext())
        {
            var parent = new Issue
            {
                IssueNumber = "CD-REPORT",
                IssueDate = new DateTime(2026, 12, 15),
                ReceivedBy = string.Empty,
                ReceivedUnit = string.Empty,
                DeliveredBy = string.Empty,
                Reason = string.Empty,
                TotalAmountText = string.Empty
            };
            context.Issues.Add(parent);
            await context.SaveChangesAsync();

            context.Issues.AddRange(
                new Issue
                {
                    IssueNumber = "PX-A-REPORT",
                    IssueDate = new DateTime(2027, 1, 1),
                    ParentIssueId = parent.Id,
                    SubordinateUnitId = unitA.Id,
                    ReceivedUnit = unitA.Name,
                    ReceivedBy = "Nguyễn Văn A",
                    DeliveredBy = string.Empty,
                    Reason = "Cấp thuốc",
                    TotalAmount = 20_000,
                    TotalAmountText = string.Empty,
                    Lines =
                    [
                        new IssueLine { ItemId = 1, Quantity = 2, UnitPrice = 10_000, Amount = 20_000 }
                    ]
                },
                new Issue
                {
                    IssueNumber = "PX-B-REPORT",
                    IssueDate = new DateTime(2027, 1, 31),
                    ParentIssueId = parent.Id,
                    SubordinateUnitId = unitB.Id,
                    ReceivedUnit = unitB.Name,
                    ReceivedBy = "Nguyễn Văn B",
                    DeliveredBy = string.Empty,
                    Reason = "Cấp vật tư",
                    TotalAmount = 30_000,
                    TotalAmountText = string.Empty,
                    Lines =
                    [
                        new IssueLine { ItemId = 2, Quantity = 3, UnitPrice = 10_000, Amount = 30_000 }
                    ]
                });
            await context.SaveChangesAsync();
        }

        var all = await database.Service.GetUnitIssueReportAsync(
            new DateTime(2027, 1, 1),
            new DateTime(2027, 1, 31));
        Assert.Equal(2, all.Count);
        Assert.Equal(5, all.Sum(x => x.TotalQuantity));
        Assert.Equal(50_000, all.Sum(x => x.TotalAmount));
        Assert.All(all, x => Assert.Equal("CD-REPORT", x.ParentIssueNumber));

        var unitOnly = await database.Service.GetUnitIssueReportAsync(
            new DateTime(2027, 1, 1),
            new DateTime(2027, 1, 31),
            unitA.Id);
        Assert.Equal("PX-A-REPORT", Assert.Single(unitOnly).IssueNumber);

        var keyword = await database.Service.GetUnitIssueReportAsync(
            new DateTime(2027, 1, 1),
            new DateTime(2027, 1, 31),
            keyword: "beta");
        Assert.Equal("B01", Assert.Single(keyword).UnitCode);
    }

    [Fact]
    public async Task Balance_issue_allows_zero_quantity_line_without_unit_child()
    {
        await using var database = await TestDatabase.CreateAsync();
        var unit = await database.Service.AddSubordinateUnitAsync(new SubordinateUnit
        {
            UnitCode = "Z0",
            Name = "Đơn vị zero",
            SortOrder = 1
        });

        await database.Service.CreateReceiptAsync(Receipt(
            "PN-ZERO",
            new DateTime(2026, 4, 1),
            "MH-ZERO",
            10,
            2_000));
        await database.Service.CreateReceiptAsync(Receipt(
            "PN-KEEP",
            new DateTime(2026, 4, 1),
            "MH-KEEP",
            10,
            2_000));

        var issue = await database.Service.CreateIssueAsync(new IssueInput
        {
            IssueNumber = "CD-ZERO",
            IssueDate = new DateTime(2026, 4, 5),
            Reason = "Có dòng SL 0",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = "MH-KEEP",
                    ItemName = "Tên MH-KEEP",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 4,
                    UnitPrice = 2_000,
                    Allocations = new Dictionary<int, decimal> { [unit.Id] = 4 }
                },
                new TransactionLineInput
                {
                    ItemCode = "MH-ZERO",
                    ItemName = "Tên MH-ZERO",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    UsageType = "",
                    Quantity = 0,
                    UnitPrice = 2_000,
                    Allocations = []
                }
            ]
        });

        await using var context = database.CreateContext();
        var balance = await context.Issues
            .Include(x => x.Lines)
            .SingleAsync(x => x.Id == issue.Id);
        Assert.Equal(2, balance.Lines.Count);
        Assert.Contains(balance.Lines, x => x.Quantity == 0);

        var children = await database.Service.GetGeneratedUnitIssuesAsync(issue.Id);
        var child = Assert.Single(children);
        Assert.Single(child.Lines);
        Assert.Equal(4, child.Lines.Single().Quantity);

        // Dòng SL = 0 không trừ tồn / không ghi sổ.
        var zeroItemId = balance.Lines.Single(x => x.Quantity == 0).ItemId;
        Assert.Equal(0, await context.StockLedgerEntries.CountAsync(x =>
            x.ItemId == zeroItemId && x.DocumentNumber == "CD-ZERO"));
        var zeroPeriod = await context.StockPeriods.SingleAsync(x => x.ItemId == zeroItemId);
        Assert.Equal(0, zeroPeriod.IssuedQuantity);
        Assert.Equal(10, zeroPeriod.ClosingQuantity);
    }

    private static ReceiptInput Receipt(
        string number,
        DateTime date,
        string itemCode,
        decimal quantity,
        decimal price) =>
        new()
        {
            ReceiptNumber = number,
            ReceiptDate = date,
            DeliveredBy = "Kế hoạch",
            DeliveredUnit = "HĐ 01",
            ReceivedBy = "Kho",
            Reason = "Nhập",
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = itemCode,
                    ItemName = $"Tên {itemCode}",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    UsageType = "Cấp trên",
                    Quantity = quantity,
                    UnitPrice = price
                }
            ]
        };

    private static IssueInput Issue(
        string number,
        DateTime date,
        string itemCode,
        decimal quantity,
        decimal price,
        Dictionary<int, decimal>? allocations = null) =>
        new()
        {
            IssueNumber = number,
            IssueDate = date,
            Lines =
            [
                new TransactionLineInput
                {
                    ItemCode = itemCode,
                    ItemName = $"Tên {itemCode}",
                    Category = "Thuốc",
                    Unit = "Hộp",
                    UsageType = "Cấp trên",
                    Quantity = quantity,
                    UnitPrice = price,
                    Allocations = allocations ?? []
                }
            ]
        };
}
