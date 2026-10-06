using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260723230000_ItemVariantUniqueKey")]
public sealed class ItemVariantUniqueKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Items_ItemName_UnitPrice",
            table: "Items");

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemName_UnitPrice_Category_UsageType",
            table: "Items",
            columns: new[] { "ItemName", "UnitPrice", "Category", "UsageType" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Items_ItemName_UnitPrice_Category_UsageType",
            table: "Items");

        migrationBuilder.CreateIndex(
            name: "IX_Items_ItemName_UnitPrice",
            table: "Items",
            columns: new[] { "ItemName", "UnitPrice" },
            unique: true);
    }
}
