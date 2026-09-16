using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Documento da troca de liberação de uma Expedição da carga (GAC-1177): amarra, por troca,
    /// a Expedição original (substituída, retirada da carga), o estorno gerado na origem (12 e,
    /// quando ela é Standard, 9) e a Expedição nova que assume a vaga vigente no destino (7 e,
    /// quando ele consome contrato, 8).
    /// </summary>
    /// <remarks>
    /// <b>Tabela nova, sem backfill.</b> <c>SHIPPING_RELEASE_CHANGES</c> não tem histórico
    /// anterior — a troca de liberação por romaneios de compensação é o desenho v2 da feature, e
    /// nada precisa ser migrado de dados existentes.
    /// <para>
    /// <b>Duas colunas em <c>STORAGE_TRANSACTIONS</c>, sentidos opostos.</b>
    /// <c>ReplacedByShippingReleaseChangeKey</c> aponta a troca que EXPULSOU o romaneio da carga
    /// (gravada só na Expedição original); <c>ShippingReleaseChangeKey</c> aponta a troca que
    /// GEROU o romaneio (estorno 12/9 ou Expedição nova 7/8). Sem essas duas colunas os guards
    /// que hoje recusam mexer num romaneio "preso" à carga (cancelamento, estorno, desvincular)
    /// não teriam como diferenciar um romaneio comum de um envolvido numa troca.
    /// </para>
    /// <para>
    /// <b>Nove FKs, nenhuma navigation property.</b> Seis colunas da entidade nova repetem o
    /// principal <c>STORAGE_TRANSACTIONS</c> (original/estorno/nova × venda/compra) e duas
    /// repetem <c>SHIPMENT_RELEASES</c> (origem/destino); mais as duas de
    /// <c>STORAGE_TRANSACTIONS</c> acima, que repetem o principal
    /// <c>SHIPPING_RELEASE_CHANGES</c>. Sem coleção inversa em nenhuma ponta — a convenção do EF
    /// não teria como parear seis/duas relações com a mesma principal sozinha, e pendurar
    /// qualquer uma na coleção errada silenciosamente inflaria o EDM do OData e confundiria
    /// consultas que hoje leem <c>StorageTransaction.ShipmentLoad</c>/<c>ShipmentRelease</c> por
    /// outro caminho. <c>NoAction</c> em todas, como o resto do projeto (Restrict gera um
    /// snapshot diferente para a mesma DDL).
    /// </para>
    /// </remarks>
    public partial class AddShippingReleaseChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SHIPPING_RELEASE_CHANGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalSalesStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalPurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnSalesStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnPurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewSalesStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewPurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    NewQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: false),
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
                    table.PrimaryKey("PK_SHIPPING_RELEASE_CHANGES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_SHIPMENT_RELEASES_SourceShipmentReleaseKey",
                        column: x => x.SourceShipmentReleaseKey,
                        principalTable: "SHIPMENT_RELEASES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_SHIPMENT_RELEASES_TargetShipmentReleaseKey",
                        column: x => x.TargetShipmentReleaseKey,
                        principalTable: "SHIPMENT_RELEASES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_NewPurchaseStorageTransactionKey",
                        column: x => x.NewPurchaseStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_NewSalesStorageTransactionKey",
                        column: x => x.NewSalesStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_OriginalPurchaseStorageTransactionKey",
                        column: x => x.OriginalPurchaseStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_OriginalSalesStorageTransactionKey",
                        column: x => x.OriginalSalesStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_ReturnPurchaseStorageTransactionKey",
                        column: x => x.ReturnPurchaseStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPPING_RELEASE_CHANGES_STORAGE_TRANSACTIONS_ReturnSalesStorageTransactionKey",
                        column: x => x.ReturnSalesStorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ReplacedByShippingReleaseChangeKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShippingReleaseChangeKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_NewPurchaseStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "NewPurchaseStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_NewSalesStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "NewSalesStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_OperationGroupKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "OperationGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_OriginalPurchaseStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "OriginalPurchaseStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_OriginalSalesStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "OriginalSalesStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_ReturnPurchaseStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "ReturnPurchaseStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_ReturnSalesStorageTransactionKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "ReturnSalesStorageTransactionKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_ShipmentLoadKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_SourceShipmentReleaseKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "SourceShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_RELEASE_CHANGES_TargetShipmentReleaseKey",
                table: "SHIPPING_RELEASE_CHANGES",
                column: "TargetShipmentReleaseKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_RELEASE_CHANGES_ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ReplacedByShippingReleaseChangeKey",
                principalTable: "SHIPPING_RELEASE_CHANGES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_RELEASE_CHANGES_ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS",
                column: "ShippingReleaseChangeKey",
                principalTable: "SHIPPING_RELEASE_CHANGES",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_RELEASE_CHANGES_ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_SHIPPING_RELEASE_CHANGES_ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "SHIPPING_RELEASE_CHANGES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ReplacedByShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ShippingReleaseChangeKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
