using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Recusa/devolução de carga: o terceiro termo do saldo, o vínculo da devolução com a carga
    /// de origem e o contexto da narrativa de frete.
    /// </summary>
    /// <remarks>
    /// <b><c>STORAGE_TRANSACTIONS.RefusedFromShipmentLoadKey</c> é coluna NOVA, e não reuso de
    /// <c>ShipmentLoadKey</c></b>: aquela significa "romaneio montado NESTA carga" e é somada
    /// por <c>ShipmentLoadsRecalculateTotalService</c> para obter o volume embarcado. Reusá-la
    /// faria a devolução aumentar o total da carga de onde a mercadoria saiu.
    /// <para>
    /// <c>SHIPMENT_LOADS.ReturnedToWarehouseQuantity</c> nasce zero e esse é o valor correto
    /// para todo o histórico — nunca houve devolução ao armazém antes desta feature, então não
    /// há backfill a fazer.
    /// </para>
    /// <para>
    /// FK sem <c>onDelete</c>, como todas as do projeto (<c>NoAction</c>). As sete colunas de
    /// <c>SHIPMENT_LOAD_MOVEMENTS</c> são o contexto que o financeiro lê para pagar o frete
    /// (cliente, local de entrega, armazém de destino e motivo) — a tabela continua sendo
    /// NARRATIVA, nada ali é lido de volta para compor saldo.
    /// </para>
    /// </remarks>
    public partial class AddShipmentLoadRefusalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedToWarehouseQuantity",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CardCode",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCardCode",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryCardName",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageTransactionKey",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseName",
                table: "SHIPMENT_LOAD_MOVEMENTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                column: "RefusedFromShipmentLoadKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOADS_RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS",
                column: "RefusedFromShipmentLoadKey",
                principalTable: "SHIPMENT_LOADS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPMENT_LOADS_RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "RefusedFromShipmentLoadKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ReturnedToWarehouseQuantity",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "CardCode",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "DeliveryCardCode",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "DeliveryCardName",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "StorageTransactionKey",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                table: "SHIPMENT_LOAD_MOVEMENTS");

            migrationBuilder.DropColumn(
                name: "WarehouseName",
                table: "SHIPMENT_LOAD_MOVEMENTS");
        }
    }
}
