using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseContractPaymentRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EarlyPaymentAuthorized",
                table: "PURCHASE_CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBarter",
                table: "PURCHASE_CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnWheels",
                table: "PURCHASE_CONTRACTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PaymentReleaseStatus",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACT_PAYMENT_RELEASES",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Department = table.Column<int>(type: "int", nullable: false),
                    Released = table.Column<bool>(type: "bit", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACT_PAYMENT_RELEASES", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACT_PAYMENT_RELEASES_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACT_PAYMENT_RELEASES_PurchaseContractKey_Department",
                table: "PURCHASE_CONTRACT_PAYMENT_RELEASES",
                columns: new[] { "PurchaseContractKey", "Department" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACT_PAYMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "EarlyPaymentAuthorized",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "IsBarter",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "IsOnWheels",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "PaymentReleaseStatus",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
