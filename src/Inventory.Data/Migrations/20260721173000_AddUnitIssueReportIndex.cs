using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20260721173000_AddUnitIssueReportIndex")]
public sealed class AddUnitIssueReportIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Issues_SubordinateUnitId_IssueDate",
            table: "Issues",
            columns: new[] { "SubordinateUnitId", "IssueDate" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Issues_SubordinateUnitId_IssueDate",
            table: "Issues");
    }
}
