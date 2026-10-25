using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20261025110000_AddStockPeriodIsHiddenInDashboard")]
public sealed class AddStockPeriodIsHiddenInDashboard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsHiddenInDashboard",
            table: "StockPeriods",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsHiddenInDashboard",
            table: "StockPeriods");
    }
}
