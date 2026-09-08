using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Suporte à liberação de embarque emitida por uma DEVOLUÇÃO ao armazém
    /// (<c>ReleaseOrigin.SalesReturn</c>): o vínculo com o romaneio que a gerou e um campo de
    /// observações para o operador.
    /// </summary>
    /// <remarks>
    /// <b><c>GeneratedByStorageTransactionKey</c> aponta a ENTRADA no armazém</b> (o romaneio
    /// <c>SalesShipmentReturn</c>), e não a nota de retorno nem a carga recusada. A entrada é uma
    /// por operação de devolução; nota e carga não são. Uma carga pode ser recusada em parcelas, e
    /// desfazer uma delas por <c>RefusedFromShipmentLoadKey</c> derrubaria as liberações das
    /// outras. Índice NÃO único de propósito: uma devolução cujos romaneios venham de contratos de
    /// compra diferentes emite uma liberação por contrato, todas apontando a mesma entrada.
    /// <para>
    /// <b><c>Comments</c></b> existe porque, sem ele, o operador que vê o saldo na Expedição de
    /// Grãos não tem nenhuma pista de que aquilo é mercadoria que voltou — nem de qual documento
    /// ou carga, nem de como o volume foi atribuído ao contrato.
    /// </para>
    /// <para>
    /// <b>Nada a backfillar.</b> A coluna <c>Origin</c> já existe como <c>int</c> com default 0;
    /// <c>SalesReturn = 2</c> é append puro e nenhuma linha histórica nasceu desta feature. As
    /// devoluções já confirmadas continuam sem liberação — destravá-las é decisão de operação, não
    /// de migration. FK sem <c>onDelete</c>, como todas as do projeto (<c>NoAction</c>): a
    /// liberação é rastro e o romaneio não pode ser apagado por baixo dela.
    /// </para>
    /// </remarks>
    public partial class AddShipmentReleaseReturnOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Comments",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES",
                column: "GeneratedByStorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_STORAGE_TRANSACTIONS_GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES",
                column: "GeneratedByStorageTransactionKey",
                principalTable: "STORAGE_TRANSACTIONS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_STORAGE_TRANSACTIONS_GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "Comments",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "GeneratedByStorageTransactionKey",
                table: "SHIPMENT_RELEASES");
        }
    }
}
