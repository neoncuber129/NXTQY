using Inventory.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Tests;

public class SchemaSelfHealTests
{
    [Fact]
    public async Task Initialize_Recreates_Missing_NoteManualAt_Without_Losing_Data()
    {
        await using var database = await TestDatabase.CreateAsync();
        var item = await database.Service.AddItemAsync(new Item
        {
            ItemCode = "HEAL-1",
            ItemName = "Hàng vá schema",
            Category = "DM",
            Unit = "cái",
            UsageType = "",
            UnitPrice = 1000m,
            Note = "giữ lại"
        });

        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Items" DROP COLUMN "NoteManualAt";""");
        }

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
        {
            await using var db = database.CreateContext();
            _ = await db.Items.AsNoTracking().Select(x => x.NoteManualAt).ToListAsync();
        });

        await database.Service.InitializeAsync();

        await using (var db = database.CreateContext())
        {
            var columns = await ReadColumnNamesAsync(db, "Items");
            Assert.Contains("NoteManualAt", columns, StringComparer.OrdinalIgnoreCase);

            var restored = await db.Items.AsNoTracking().SingleAsync(x => x.Id == item.Id);
            Assert.Equal("giữ lại", restored.Note);
            Assert.Null(restored.NoteManualAt);
        }
    }

    [Fact]
    public async Task Initialize_Recreates_Missing_StockPeriod_Note()
    {
        await using var database = await TestDatabase.CreateAsync();

        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "StockPeriods" DROP COLUMN "Note";""");
        }

        await database.Service.InitializeAsync();

        await using (var db = database.CreateContext())
        {
            var columns = await ReadColumnNamesAsync(db, "StockPeriods");
            Assert.Contains("Note", columns, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<List<string>> ReadColumnNamesAsync(Inventory.Data.InventoryDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
