#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableShippingTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHIPPING_TRANSACTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseStorageKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesStorageKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPPING_TRANSACTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPPING_TRANSACTIONS_STORAGE_TRANSACTIONS_PurchaseStorageTransactionKey",
                        column: x => x.PurchaseStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_TRANSACTIONS_STORAGE_TRANSACTIONS_SalesStorageTransactionKey",
                        column: x => x.SalesStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_TRANSACTIONS_PurchaseStorageTransactionKey",
                table: "SHIPPING_TRANSACTIONS",
                column: "PurchaseStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_TRANSACTIONS_SalesStorageTransactionKey",
                table: "SHIPPING_TRANSACTIONS",
                column: "SalesStorageTransactionKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPPING_TRANSACTIONS");
        }
    }
}
