using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableSalesContractsAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedVolume",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SALES_CONTRACTS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_ALLOCATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Volume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    InvoiceUnitPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    ContractPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    PriceDifference = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Origin = table.Column<int>(type: "int", nullable: false),
                    ReallocationGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_SALES_CONTRACTS_ALLOCATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_ALLOCATIONS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_ALLOCATIONS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_ALLOCATIONS_SALES_SHIPMENT_RELEASES_SalesShipmentReleaseKey",
                        column: x => x.SalesShipmentReleaseKey,
                        principalTable: "SALES_SHIPMENT_RELEASES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_ReallocationGroupKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "ReallocationGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_SalesContractKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "SalesContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_SalesInvoiceItemKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ALLOCATIONS_SalesShipmentReleaseKey",
                table: "SALES_CONTRACTS_ALLOCATIONS",
                column: "SalesShipmentReleaseKey");

            // Backfill do ledger a partir das invoices existentes.
            // Volume assinado: Normal (InvoiceType=0) positivo, Devolução (InvoiceType=1) negativa.
            // Elegíveis: Normal Confirmed(1)/Returned(3); Return Confirmed(1) — mesma regra dos
            // computados legados TotalVolumeOutgoing/TotalVolumeIncoming.
            // Devolução não copia SalesShipmentReleaseKey no item — herda do item de origem (COALESCE).
            // Origin = 3 (Backfill).
            migrationBuilder.Sql(@"
                INSERT INTO SALES_CONTRACTS_ALLOCATIONS
                    ([Key], SalesContractKey, SalesInvoiceItemKey, SalesShipmentReleaseKey, Volume,
                     InvoiceUnitPrice, ContractPrice, PriceDifference, Origin, ReallocationGroupKey,
                     CreatedAt, CreatedBy, ApprovedAt, ApprovedBy)
                SELECT NEWID(), i.SalesContractKey, i.[Key],
                       COALESCE(i.SalesShipmentReleaseKey, oi.SalesShipmentReleaseKey),
                       CASE WHEN inv.InvoiceType = 1 THEN -i.Quantity ELSE i.Quantity END,
                       i.UnitPrice, c.Price,
                       ROUND((CASE WHEN inv.InvoiceType = 1 THEN -i.Quantity ELSE i.Quantity END)
                             * (c.Price - i.UnitPrice), 2),
                       3, NULL,
                       GETDATE(), 'migration', inv.ApprovedAt, inv.ApprovedBy
                FROM SALES_INVOICES_ITEMS i
                JOIN SALES_INVOICES inv ON inv.[Key] = i.SalesInvoiceKey
                JOIN SALES_CONTRACTS c ON c.[Key] = i.SalesContractKey
                LEFT JOIN SALES_INVOICES_ITEMS oi ON oi.[Key] = i.SalesInvoiceItemOriginKey
                WHERE i.SalesContractKey IS NOT NULL
                  AND ( (inv.InvoiceType = 0 AND inv.InvoiceStatus IN (1, 3))
                     OR (inv.InvoiceType = 1 AND inv.InvoiceStatus = 1) );");

            // Backfill de AllocatedVolume com o fator efetivo por item
            // (item Closed conta NetQuantity/Quantity — preserva a semântica dos computados
            // legados, em que quebra de entrega devolve saldo ao contrato).
            migrationBuilder.Sql(@"
                UPDATE C SET C.AllocatedVolume = ROUND(ISNULL((
                    SELECT SUM(a.Volume * CASE WHEN i.DeliveryStatus = 1 AND i.Quantity <> 0
                        THEN (i.DeliveredQuantity - i.QuantityLoss) / i.Quantity ELSE 1 END)
                    FROM SALES_CONTRACTS_ALLOCATIONS a
                    JOIN SALES_INVOICES_ITEMS i ON i.[Key] = a.SalesInvoiceItemKey
                    WHERE a.SalesContractKey = C.[Key]), 0), 3)
                FROM SALES_CONTRACTS C;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropColumn(
                name: "AllocatedVolume",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SALES_CONTRACTS");
        }
    }
}
