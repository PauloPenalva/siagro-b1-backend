using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseContractChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_CHANGE_LOGS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_CHANGE_LOGS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_CHANGE_LOGS",
                column: "PurchaseContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_CHANGE_LOGS");
        }
    }
}
