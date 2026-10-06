using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260725123000_AddItemNoteManualAt")]
public sealed class AddItemNoteManualAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "NoteManualAt",
            table: "Items",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "NoteManualAt",
            table: "Items");
    }
}
