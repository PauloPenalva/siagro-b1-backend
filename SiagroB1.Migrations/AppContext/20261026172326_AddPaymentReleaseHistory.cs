using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPaymentReleaseHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "PURCHASE_CONTRACT_PAYMENT_RELEASES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Department = table.Column<int>(type: "int", nullable: false),
                    Released = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY_PurchaseContractKey_Department_ChangedAt",
                table: "PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY",
                columns: new[] { "PurchaseContractKey", "Department", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACT_PAYMENT_RELEASE_HISTORY");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "PURCHASE_CONTRACT_PAYMENT_RELEASES");
        }
    }
}
