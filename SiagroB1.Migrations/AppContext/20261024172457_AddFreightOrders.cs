using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFreightOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FREIGHT_ORDERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TakerCardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    SenderCardCode = table.Column<string>(type: "VARCHAR(15)", nullable: true),
                    RecipientCardCode = table.Column<string>(type: "VARCHAR(15)", nullable: true),
                    StartMunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    StartState = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    EndMunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    EndState = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    ContractedWeightKg = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    PriceMethod = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentConditionCode = table.Column<int>(type: "int", nullable: true),
                    UsageCode = table.Column<int>(type: "int", nullable: true),
                    Observation = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedManually = table.Column<bool>(type: "bit", nullable: false),
                    TransportedWeightKg = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FREIGHT_ORDERS", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENTS_FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS",
                column: "FreightOrderKey");

            migrationBuilder.CreateIndex(
                name: "IX_FREIGHT_ORDERS_BranchCode_Code",
                table: "FREIGHT_ORDERS",
                columns: new[] { "BranchCode", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TRANSPORT_DOCUMENTS_FREIGHT_ORDERS_FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS",
                column: "FreightOrderKey",
                principalTable: "FREIGHT_ORDERS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TRANSPORT_DOCUMENTS_FREIGHT_ORDERS_FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropTable(
                name: "FREIGHT_ORDERS");

            migrationBuilder.DropIndex(
                name: "IX_TRANSPORT_DOCUMENTS_FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "FreightOrderKey",
                table: "TRANSPORT_DOCUMENTS");
        }
    }
}
