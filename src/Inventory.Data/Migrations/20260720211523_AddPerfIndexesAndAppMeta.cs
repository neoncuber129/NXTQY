using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerfIndexesAndAppMeta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppMeta",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppMeta", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_TransactionType_DocumentNumber",
                table: "StockLedgerEntries",
                columns: new[] { "TransactionType", "DocumentNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_ReceiptDate",
                table: "Receipts",
                column: "ReceiptDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptLines_ItemId",
                table: "ReceiptLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_IssueDate",
                table: "Issues",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLines_ItemId",
                table: "IssueLines",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppMeta");

            migrationBuilder.DropIndex(
                name: "IX_StockLedgerEntries_TransactionType_DocumentNumber",
                table: "StockLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_Receipts_ReceiptDate",
                table: "Receipts");

            migrationBuilder.DropIndex(
                name: "IX_ReceiptLines_ItemId",
                table: "ReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_Issues_IssueDate",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_IssueLines_ItemId",
                table: "IssueLines");
        }
    }
}
