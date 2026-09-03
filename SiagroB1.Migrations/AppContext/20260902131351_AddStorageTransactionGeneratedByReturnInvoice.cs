using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Vínculo do romaneio de devolução ao armazém com o documento de RETORNO que o gerou, no
    /// fluxo legado (documento de saída sem carga).
    /// </summary>
    /// <remarks>
    /// <b>Aponta o RETORNO, e não a nota de origem</b>: uma nota pode ser retornada em parcelas,
    /// cada uma com sua devolução ao armazém, e pela origem o estorno de uma delas não teria como
    /// saber qual entrada cancelar.
    /// <para>
    /// <b>Coluna NOVA, e não reuso de <c>SalesInvoiceKey</c></b>: aquela significa "romaneio
    /// FATURADO nesta nota" e é o que <c>ShipmentBillingTransactionGuardService</c> lê para
    /// recusar refaturamento. Nem <c>ReturnInvoiceKey</c>, que é o discriminador do estorno.
    /// </para>
    /// <para>
    /// É o análogo legado de <c>RefusedFromShipmentLoadKey</c> e existe pelo mesmo motivo: dar aos
    /// guards de <c>StorageTransactionsCancelService</c>/<c>StorageTransactionsReverseService</c>
    /// um vínculo que reconheçam. A devolução do fluxo legado não tem carga, então tem aquela
    /// coluna nula e escaparia dos dois — cancelá-la pela tela de Romaneios derrubaria o crédito
    /// do armazém em silêncio.
    /// </para>
    /// <para>
    /// Nasce nula em todo o histórico, e esse é o valor correto: nunca houve devolução ao armazém
    /// pelo fluxo legado antes desta feature. Nada a backfillar. FK sem <c>onDelete</c>, como
    /// todas as do projeto (<c>NoAction</c>).
    /// </para>
    /// </remarks>
    public partial class AddStorageTransactionGeneratedByReturnInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                column: "GeneratedByReturnInvoiceKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS",
                column: "GeneratedByReturnInvoiceKey",
                principalTable: "SALES_INVOICES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SALES_INVOICES_GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "GeneratedByReturnInvoiceKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
