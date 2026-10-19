using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseTransactionChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_TRANSACTION_CHANGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalCardCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    OriginalWarehouseCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    NewCardCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    NewWarehouseCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    AllocatedVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_PURCHASE_TRANSACTION_CHANGES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_TRANSACTION_CHANGES_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_TRANSACTION_CHANGES_STORAGE_TRANSACTIONS_NewStorageTransactionKey",
                        column: x => x.NewStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_TRANSACTION_CHANGES_STORAGE_TRANSACTIONS_OriginalStorageTransactionKey",
                        column: x => x.OriginalStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_TRANSACTION_CHANGES_STORAGE_TRANSACTIONS_ReturnStorageTransactionKey",
                        column: x => x.ReturnStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseTransactionChangeKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_TRANSACTION_CHANGES_NewStorageTransactionKey",
                table: "PURCHASE_TRANSACTION_CHANGES",
                column: "NewStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_TRANSACTION_CHANGES_OriginalStorageTransactionKey",
                table: "PURCHASE_TRANSACTION_CHANGES",
                column: "OriginalStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_TRANSACTION_CHANGES_PurchaseContractKey",
                table: "PURCHASE_TRANSACTION_CHANGES",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_TRANSACTION_CHANGES_ReturnStorageTransactionKey",
                table: "PURCHASE_TRANSACTION_CHANGES",
                column: "ReturnStorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_TRANSACTION_CHANGES_PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "PurchaseTransactionChangeKey",
                principalTable: "PURCHASE_TRANSACTION_CHANGES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PURCHASE_TRANSACTION_CHANGES_PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "PURCHASE_TRANSACTION_CHANGES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "PurchaseTransactionChangeKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
