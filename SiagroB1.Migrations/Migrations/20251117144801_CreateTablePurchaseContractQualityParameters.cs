using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTablePurchaseContractQualityParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureKey",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.RenameColumn(
                name: "UnitOfMeasureKey",
                table: "PURCHASE_CONTRACTS",
                newName: "UnitOfMeasureCode");

            migrationBuilder.RenameColumn(
                name: "ProductKey",
                table: "PURCHASE_CONTRACTS",
                newName: "ItemCode");

            migrationBuilder.RenameColumn(
                name: "BusinessParterKey",
                table: "PURCHASE_CONTRACTS",
                newName: "CardCode");

            migrationBuilder.RenameIndex(
                name: "IX_PURCHASE_CONTRACTS_UnitOfMeasureKey",
                table: "PURCHASE_CONTRACTS",
                newName: "IX_PURCHASE_CONTRACTS_UnitOfMeasureCode");

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(15)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityAttribCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    MaxLimitRate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribCode",
                        column: x => x.QualityAttribCode,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_QUALITY_PARAMETERS_QualityAttribCode",
                table: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS",
                column: "QualityAttribCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES");

            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_QUALITY_PARAMETERS");

            migrationBuilder.RenameColumn(
                name: "UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                newName: "UnitOfMeasureKey");

            migrationBuilder.RenameColumn(
                name: "ItemCode",
                table: "PURCHASE_CONTRACTS",
                newName: "ProductKey");

            migrationBuilder.RenameColumn(
                name: "CardCode",
                table: "PURCHASE_CONTRACTS",
                newName: "BusinessParterKey");

            migrationBuilder.RenameIndex(
                name: "IX_PURCHASE_CONTRACTS_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                newName: "IX_PURCHASE_CONTRACTS_UnitOfMeasureKey");

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreationDate",
                table: "PURCHASE_CONTRACTS",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(15)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureKey",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureKey",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_TAXES_PURCHASE_CONTRACTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_TAXES",
                column: "PurchaseContractKey",
                principalTable: "PURCHASE_CONTRACTS",
                principalColumn: "Key",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
