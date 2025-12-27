#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTablePurchaseContractsBrokers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_BROKERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Commission = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ComissionUmCode = table.Column<string>(type: "VARCHAR(4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_BROKERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_BROKERS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_BROKERS_UNITS_OF_MEASURE_ComissionUmCode",
                        column: x => x.ComissionUmCode,
                        principalTable: "UNITS_OF_MEASURE",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_BROKERS_ComissionUmCode",
                table: "PURCHASE_CONTRACTS_BROKERS",
                column: "ComissionUmCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_BROKERS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_BROKERS",
                column: "PurchaseContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_BROKERS");
        }
    }
}
