using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubordinateUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueLineId = table.Column<int>(type: "INTEGER", nullable: false),
                    SubordinateUnitId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueLineAllocations_IssueLines_IssueLineId",
                        column: x => x.IssueLineId,
                        principalTable: "IssueLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubordinateUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubordinateUnits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueLineAllocations_IssueLineId_SubordinateUnitId",
                table: "IssueLineAllocations",
                columns: new[] { "IssueLineId", "SubordinateUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubordinateUnits_Name",
                table: "SubordinateUnits",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueLineAllocations");

            migrationBuilder.DropTable(
                name: "SubordinateUnits");
        }
    }
}
