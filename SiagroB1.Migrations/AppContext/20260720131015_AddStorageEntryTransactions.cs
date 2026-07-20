using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddStorageEntryTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "STORAGE_ENTRY_TRANSACTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAddressCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AllocatedVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ReceiptNetWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
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
                    table.PrimaryKey("PK_STORAGE_ENTRY_TRANSACTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_ENTRY_TRANSACTIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_STORAGE_ENTRY_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressCode",
                        column: x => x.StorageAddressCode,
                        principalTable: "STORAGE_ADDRESSES",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_STORAGE_ENTRY_TRANSACTIONS_STORAGE_TRANSACTIONS_PurchaseStorageTransactionKey",
                        column: x => x.PurchaseStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_STORAGE_ENTRY_TRANSACTIONS_STORAGE_TRANSACTIONS_ReceiptStorageTransactionKey",
                        column: x => x.ReceiptStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ENTRY_TRANSACTIONS_PurchaseContractKey",
                table: "STORAGE_ENTRY_TRANSACTIONS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ENTRY_TRANSACTIONS_PurchaseStorageTransactionKey",
                table: "STORAGE_ENTRY_TRANSACTIONS",
                column: "PurchaseStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ENTRY_TRANSACTIONS_ReceiptStorageTransactionKey",
                table: "STORAGE_ENTRY_TRANSACTIONS",
                column: "ReceiptStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ENTRY_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_ENTRY_TRANSACTIONS",
                column: "StorageAddressCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STORAGE_ENTRY_TRANSACTIONS");
        }
    }
}
