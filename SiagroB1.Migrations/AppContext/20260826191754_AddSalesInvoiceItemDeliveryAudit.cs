using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesInvoiceItemDeliveryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SALES_INVOICES_ITEMS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceItemKey",
                table: "SALES_INVOICES_CHANGE_LOGS",
                type: "uniqueidentifier",
                nullable: true);

            // O diálogo da conferência consulta o log FILTRANDO por esta chave (entity set, e
            // não rota aninhada). Índice filtrado: as linhas de cabeçalho ficam de fora.
            migrationBuilder.Sql(@"
                CREATE INDEX IX_SALES_INVOICES_CHANGE_LOGS_SalesInvoiceItemKey
                ON SALES_INVOICES_CHANGE_LOGS (SalesInvoiceItemKey)
                WHERE SalesInvoiceItemKey IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IX_SALES_INVOICES_CHANGE_LOGS_SalesInvoiceItemKey ON SALES_INVOICES_CHANGE_LOGS;");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceItemKey",
                table: "SALES_INVOICES_CHANGE_LOGS");
        }
    }
}
