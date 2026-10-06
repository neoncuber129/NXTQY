using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260725120000_AddStockPeriodNote")]
public sealed class AddStockPeriodNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Note",
            table: "StockPeriods",
            type: "TEXT",
            maxLength: 1000,
            nullable: true);

        // Dữ liệu cũ: ghi chú tồn đầu từng lưu trên Items.Note — chuyển sang kỳ có tồn đầu > 0.
        migrationBuilder.Sql(
            """
            UPDATE StockPeriods
            SET Note = (
                SELECT i.Note
                FROM Items i
                WHERE i.Id = StockPeriods.ItemId
                  AND i.Note IS NOT NULL
                  AND TRIM(i.Note) <> ''
            )
            WHERE OpeningQuantity <> 0
              AND (Note IS NULL OR TRIM(Note) = '')
              AND EXISTS (
                  SELECT 1 FROM Items i
                  WHERE i.Id = StockPeriods.ItemId
                    AND i.Note IS NOT NULL
                    AND TRIM(i.Note) <> ''
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Note",
            table: "StockPeriods");
    }
}
