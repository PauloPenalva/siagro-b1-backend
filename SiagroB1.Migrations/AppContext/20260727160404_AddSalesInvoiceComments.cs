using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Comentários do documento de saída e o log de alterações que os registra: anotações com data,
    /// hora, autor e texto, mantidas na tela Detail e editáveis a qualquer tempo — inclusive em
    /// documento confirmado ou cancelado.
    ///
    /// As duas tabelas vêm na mesma migration porque nascem da mesma feature: o documento não tinha
    /// log de alterações nenhum, e ele é criado aqui justamente para receber as linhas de
    /// comentário. Por enquanto SALES_INVOICES_CHANGE_LOGS só recebe o campo <c>Comment</c>; a
    /// estrutura é a mesma do log do contrato para poder receber outros campos depois.
    ///
    /// <c>CommentText</c> é VARCHAR(500) de propósito: casa com <c>OldValue</c>/<c>NewValue</c> do
    /// log, então nenhuma linha de log sai truncada.
    ///
    /// Não confundir com a coluna <c>Comments</c> de SALES_INVOICES, que é a "Observação" do
    /// cabeçalho e continua existindo — é por causa dela que a navegação no modelo se chama
    /// <c>CommentEntries</c>.
    /// </summary>
    public partial class AddSalesInvoiceComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SALES_INVOICES_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICES_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICES_CHANGE_LOGS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SALES_INVOICES_COMMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommentedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CommentText = table.Column<string>(type: "VARCHAR(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_INVOICES_COMMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_INVOICES_COMMENTS_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_CHANGE_LOGS_SalesInvoiceKey",
                table: "SALES_INVOICES_CHANGE_LOGS",
                column: "SalesInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_COMMENTS_SalesInvoiceKey",
                table: "SALES_INVOICES_COMMENTS",
                column: "SalesInvoiceKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_INVOICES_CHANGE_LOGS");

            migrationBuilder.DropTable(
                name: "SALES_INVOICES_COMMENTS");
        }
    }
}
