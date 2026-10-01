using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// GAC-1171 (rateio): o ticket de descarga vira cabeçalho com a tabela de rateio, a marca manual
    /// IsDischarged sai e ReturnedQuantity nasce com o histórico.
    /// </summary>
    /// <remarks>
    /// ⚠️ Editada à mão: o EF gera os Drop antes do CreateTable, o que apagaria o vínculo nota/linha
    /// dos tickets antes de copiá-lo para o rateio. A ordem abaixo copia primeiro e apaga depois.
    /// </remarks>
    public partial class AddShipmentLoadDischargeItemsAndReturnedQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Rateio do ticket (GAC-1171, rateio): tabela nova.
            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DischargeKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_DISCHARGE_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGE_ITEMS_SHIPMENT_LOAD_DISCHARGES_DischargeKey",
                        column: x => x.DischargeKey,
                        principalTable: "SHIPMENT_LOAD_DISCHARGES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_DischargeKey_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                columns: new[] { "DischargeKey", "SalesInvoiceItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGE_ITEMS_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGE_ITEMS",
                column: "SalesInvoiceKey");

            // 2) Cada ticket existente vira cabeçalho + UMA linha, com o peso inteiro. Nenhum dado se
            //    perde. Ticket sem nota/linha não deveria existir (a action sempre exigiu as duas); se
            //    existir, fica sem rateio e aparece na aba com a coluna Documentos vazia.
            migrationBuilder.Sql(@"
INSERT INTO SHIPMENT_LOAD_DISCHARGE_ITEMS ([Key], DischargeKey, SalesInvoiceKey, SalesInvoiceItemKey, Quantity)
SELECT NEWID(), d.[Key], d.SalesInvoiceKey, d.SalesInvoiceItemKey, d.DischargedQuantity
FROM SHIPMENT_LOAD_DISCHARGES d
WHERE d.SalesInvoiceKey IS NOT NULL AND d.SalesInvoiceItemKey IS NOT NULL;");

            // 3) O vínculo nota/linha sai do ticket.
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES");

            // 4) A Descarregada deixa de ter marca manual: Descarregada (8) volta a Faturada (2).
            //    "Recalcular Saldo" (ou a próxima escrita de ticket) reaplica a regra nova. Sem backfill
            //    em C#, pela mesma decisão de 23/09.
            migrationBuilder.Sql("UPDATE SHIPMENT_LOADS SET Status = 2 WHERE Status = 8;");

            migrationBuilder.DropColumn(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS");

            // 5) Quantidade devolvida (persistida-derivada) e o histórico dela: devoluções CONFIRMADAS
            //    (InvoiceType 1 = Return, InvoiceStatus 1 = Confirmed; enums gravados como int).
            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql(@"
UPDATE origin SET ReturnedQuantity = returned.Quantity
FROM SALES_INVOICES_ITEMS origin
CROSS APPLY (
    SELECT ISNULL(SUM(ri.Quantity), 0) AS Quantity
    FROM SALES_INVOICES_ITEMS ri
    INNER JOIN SALES_INVOICES r ON r.[Key] = ri.SalesInvoiceKey
    WHERE ri.SalesInvoiceItemOriginKey = origin.[Key]
      AND r.InvoiceType = 1
      AND r.InvoiceStatus = 1
) returned
WHERE returned.Quantity <> 0;

UPDATE inv SET ReturnedQuantity = items.Quantity
FROM SALES_INVOICES inv
CROSS APPLY (
    SELECT ISNULL(SUM(i.ReturnedQuantity), 0) AS Quantity
    FROM SALES_INVOICES_ITEMS i
    WHERE i.SalesInvoiceKey = inv.[Key]
) items
WHERE items.Quantity <> 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.AddColumn<bool>(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                type: "uniqueidentifier",
                nullable: true);

            // ⚠️ COM PERDA: o ticket antigo aponta UMA linha. O ticket rateado em várias volta
            // apontando a de maior peso e mantém o peso total.
            migrationBuilder.Sql(@"
UPDATE d SET SalesInvoiceKey = firstLine.SalesInvoiceKey, SalesInvoiceItemKey = firstLine.SalesInvoiceItemKey
FROM SHIPMENT_LOAD_DISCHARGES d
CROSS APPLY (
    SELECT TOP 1 i.SalesInvoiceKey, i.SalesInvoiceItemKey
    FROM SHIPMENT_LOAD_DISCHARGE_ITEMS i
    WHERE i.DischargeKey = d.[Key]
    ORDER BY i.Quantity DESC
) firstLine;");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_DISCHARGE_ITEMS");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceItemKey",
                principalTable: "SALES_INVOICES_ITEMS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceKey",
                principalTable: "SALES_INVOICES",
                principalColumn: "Key");
        }
    }
}
