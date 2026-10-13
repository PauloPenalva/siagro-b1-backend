using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Conciliação fiscal de compras (spec 2026-10-13 §3): tabela do vínculo NF × romaneio e a confirmação manual no
    /// romaneio. Conferida à mão contra o drift do snapshot: só operações desta feature.
    /// </summary>
    public partial class AddPurchaseFiscalReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalReconciledAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalReconciledBy",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_FISCAL_RECONCILIATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(19,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_FISCAL_RECONCILIATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_FISCAL_RECONCILIATIONS_PURCHASE_INVOICES_ITEMS_PurchaseInvoiceItemKey",
                        column: x => x.PurchaseInvoiceItemKey,
                        principalTable: "PURCHASE_INVOICES_ITEMS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PURCHASE_FISCAL_RECONCILIATIONS_STORAGE_TRANSACTIONS_StorageTransactionKey",
                        column: x => x.StorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_FISCAL_RECONCILIATIONS_PurchaseInvoiceItemKey_StorageTransactionKey",
                table: "PURCHASE_FISCAL_RECONCILIATIONS",
                columns: new[] { "PurchaseInvoiceItemKey", "StorageTransactionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_FISCAL_RECONCILIATIONS_StorageTransactionKey",
                table: "PURCHASE_FISCAL_RECONCILIATIONS",
                column: "StorageTransactionKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_FISCAL_RECONCILIATIONS");

            migrationBuilder.DropColumn(
                name: "FiscalReconciledAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "FiscalReconciledBy",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
