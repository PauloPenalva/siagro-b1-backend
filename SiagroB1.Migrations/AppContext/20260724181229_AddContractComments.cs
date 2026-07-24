using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Comentários do contrato de compra e de venda: anotações com data, hora, autor e texto,
    /// mantidas na tela Detail e editáveis a qualquer tempo.
    ///
    /// As duas tabelas vêm na mesma migration porque nascem da mesma feature — o par é sempre
    /// espelhado.
    ///
    /// <c>CommentText</c> é VARCHAR(500) de propósito: casa com <c>OldValue</c>/<c>NewValue</c> das
    /// tabelas de log de alterações, então nenhuma linha de log sai truncada.
    ///
    /// Não confundir com a coluna <c>Comments</c> dos cabeçalhos ({PURCHASE,SALES}_CONTRACTS), que é
    /// a "Observação" e continua existindo — é por causa dela que a navegação no modelo se chama
    /// <c>CommentEntries</c>.
    /// </summary>
    public partial class AddContractComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_COMMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommentedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CommentText = table.Column<string>(type: "VARCHAR(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_COMMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_COMMENTS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_COMMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommentedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CommentText = table.Column<string>(type: "VARCHAR(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_COMMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_COMMENTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_COMMENTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_COMMENTS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_COMMENTS_SalesContractKey",
                table: "SALES_CONTRACTS_COMMENTS",
                column: "SalesContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_COMMENTS");

            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_COMMENTS");
        }
    }
}
