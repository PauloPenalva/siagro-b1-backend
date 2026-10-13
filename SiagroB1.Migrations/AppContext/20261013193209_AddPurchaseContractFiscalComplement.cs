using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseContractFiscalComplement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACT_FISCAL_COMPLEMENTS",
                columns: table => new
                {
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsageCode = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACT_FISCAL_COMPLEMENTS", x => x.PurchaseContractKey);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACT_FISCAL_COMPLEMENTS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACT_FISCAL_COMPLEMENTS");
        }
    }
}
