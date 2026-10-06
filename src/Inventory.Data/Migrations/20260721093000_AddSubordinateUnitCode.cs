using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260721093000_AddSubordinateUnitCode")]
public sealed class AddSubordinateUnitCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "UnitCode",
            table: "SubordinateUnits",
            type: "TEXT",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_SubordinateUnits_UnitCode",
            table: "SubordinateUnits",
            column: "UnitCode");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_SubordinateUnits_UnitCode",
            table: "SubordinateUnits");

        migrationBuilder.DropColumn(
            name: "UnitCode",
            table: "SubordinateUnits");
    }
}
