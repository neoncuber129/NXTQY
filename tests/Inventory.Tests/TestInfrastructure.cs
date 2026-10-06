using Inventory.Data;
using Inventory.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public sealed class TestDbContextFactory(DbContextOptions<InventoryDbContext> options)
    : IDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext() => new(options);
}

public sealed class TestDatabase : IAsyncDisposable
{
    private TestDatabase(string directoryPath, string databasePath, TestDbContextFactory factory)
    {
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
        Factory = factory;
        Service = new InventoryService(factory);
    }

    public string DirectoryPath { get; }
    public string DatabasePath { get; }
    public TestDbContextFactory Factory { get; }
    public InventoryService Service { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "InventoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "inventory-test.db");
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .EnableDetailedErrors()
            .Options;
        var database = new TestDatabase(
            directory,
            databasePath,
            new TestDbContextFactory(options));
        await database.Service.InitializeAsync();
        return database;
    }

    public InventoryDbContext CreateContext() => Factory.CreateDbContext();

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // A failed test can leave a transient SQLite handle. The unique temp folder
            // prevents it from contaminating any later test.
        }
    }
}

public sealed record LargeSeedResult(
    int ItemCount,
    int ReceiptCount,
    int IssueCount,
    int Year,
    decimal ExpectedTotalClosing);

public static class LargeDataSeeder
{
    public static LargeSeedResult Seed(
        string databasePath,
        int itemCount = 10_000,
        int receiptCount = 25_000,
        int issueCount = 25_000,
        int year = 2026)
    {
        var receivedByItem = new int[itemCount];
        var issuedByItem = new int[itemCount];
        for (var i = 0; i < receiptCount; i++)
        {
            receivedByItem[i % itemCount] += 2;
        }

        for (var i = 0; i < issueCount; i++)
        {
            issuedByItem[i % itemCount] += 1;
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var itemCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO Items
                       (Id, ItemCode, ItemName, Category, Unit, UsageType, UnitPrice, Note, NoteManualAt)
                   VALUES
                       ($id, $code, $name, 'Stress', 'Hộp', '', $price, NULL, NULL);
                   """,
                   "$id", "$code", "$name", "$price"))
        {
            for (var i = 0; i < itemCount; i++)
            {
                Set(itemCommand, "$id", i + 1);
                Set(itemCommand, "$code", $"MH{i + 1:D5}");
                Set(itemCommand, "$name", $"Mặt hàng tải lớn {i + 1:D5}");
                Set(itemCommand, "$price", 1_000 + i % 100);
                itemCommand.ExecuteNonQuery();
            }
        }

        using (var periodCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO StockPeriods
                       (Id, Year, ItemId, OpeningQuantity, ReceivedQuantity, IssuedQuantity,
                        ClosingQuantity, UnitPrice, IsManualAdjustment, Note)
                   VALUES
                       ($id, $year, $itemId, 100, $received, $issued, $closing, $price, 0, NULL);
                   """,
                   "$id", "$year", "$itemId", "$received", "$issued", "$closing", "$price"))
        {
            for (var i = 0; i < itemCount; i++)
            {
                var closing = 100 + receivedByItem[i] - issuedByItem[i];
                Set(periodCommand, "$id", i + 1);
                Set(periodCommand, "$year", year);
                Set(periodCommand, "$itemId", i + 1);
                Set(periodCommand, "$received", receivedByItem[i]);
                Set(periodCommand, "$issued", issuedByItem[i]);
                Set(periodCommand, "$closing", closing);
                Set(periodCommand, "$price", 1_000 + i % 100);
                periodCommand.ExecuteNonQuery();
            }
        }

        using (var receiptCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO Receipts
                       (Id, ReceiptNumber, ReceiptDate, DeliveredBy, DeliveredUnit,
                        ReceivedBy, Reason, TotalAmount, TotalAmountText)
                   VALUES
                       ($id, $number, $date, 'KH', 'HD', 'Kho', 'Stress', $amount, '');
                   """,
                   "$id", "$number", "$date", "$amount"))
        using (var lineCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO ReceiptLines
                       (Id, ReceiptId, ItemId, Quantity, UnitPrice, Amount, Note)
                   VALUES
                       ($id, $documentId, $itemId, 2, $price, $amount, NULL);
                   """,
                   "$id", "$documentId", "$itemId", "$price", "$amount"))
        {
            for (var i = 0; i < receiptCount; i++)
            {
                var id = i + 1;
                var itemId = i % itemCount + 1;
                var price = 1_000 + (itemId - 1) % 100;
                var amount = 2 * price;
                Set(receiptCommand, "$id", id);
                Set(receiptCommand, "$number", $"PN-STRESS-{id:D5}");
                Set(receiptCommand, "$date", new DateTime(year, 1, 1).AddDays(i % 365));
                Set(receiptCommand, "$amount", amount);
                receiptCommand.ExecuteNonQuery();

                Set(lineCommand, "$id", id);
                Set(lineCommand, "$documentId", id);
                Set(lineCommand, "$itemId", itemId);
                Set(lineCommand, "$price", price);
                Set(lineCommand, "$amount", amount);
                lineCommand.ExecuteNonQuery();
            }
        }

        using (var issueCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO Issues
                       (Id, IssueNumber, IssueDate, ReceivedBy, ReceivedUnit, DeliveredBy,
                        Reason, TotalAmount, TotalAmountText, ParentIssueId, SubordinateUnitId)
                   VALUES
                       ($id, $number, $date, 'Đơn vị', '', 'Kho', 'Stress',
                        $amount, '', NULL, NULL);
                   """,
                   "$id", "$number", "$date", "$amount"))
        using (var lineCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   INSERT INTO IssueLines
                       (Id, IssueId, ItemId, Quantity, UnitPrice, Amount, Note)
                   VALUES
                       ($id, $documentId, $itemId, 1, $price, $amount, NULL);
                   """,
                   "$id", "$documentId", "$itemId", "$price", "$amount"))
        {
            for (var i = 0; i < issueCount; i++)
            {
                var id = i + 1;
                var itemId = i % itemCount + 1;
                var price = 1_000 + (itemId - 1) % 100;
                Set(issueCommand, "$id", id);
                Set(issueCommand, "$number", $"PX-STRESS-{id:D5}");
                Set(issueCommand, "$date", new DateTime(year, 1, 1).AddDays(i % 365));
                Set(issueCommand, "$amount", price);
                issueCommand.ExecuteNonQuery();

                Set(lineCommand, "$id", id);
                Set(lineCommand, "$documentId", id);
                Set(lineCommand, "$itemId", itemId);
                Set(lineCommand, "$price", price);
                Set(lineCommand, "$amount", price);
                lineCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        var expectedTotalClosing = Enumerable.Range(0, itemCount)
            .Sum(i => (decimal)(100 + receivedByItem[i] - issuedByItem[i]));
        return new LargeSeedResult(itemCount, receiptCount, issueCount, year, expectedTotalClosing);
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params string[] parameterNames)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameterName in parameterNames)
        {
            command.Parameters.Add(new SqliteParameter(parameterName, null));
        }

        command.Prepare();
        return command;
    }

    private static void Set(SqliteCommand command, string parameterName, object value) =>
        command.Parameters[parameterName].Value = value;
}
