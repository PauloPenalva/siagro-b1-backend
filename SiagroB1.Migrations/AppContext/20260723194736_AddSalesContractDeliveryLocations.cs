using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesContractDeliveryLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_DELIVERY_LOCATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_DELIVERY_LOCATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_DELIVERY_LOCATIONS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_DELIVERY_LOCATIONS_SalesContractKey",
                table: "SALES_CONTRACTS_DELIVERY_LOCATIONS",
                column: "SalesContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_DELIVERY_LOCATIONS");
        }
    }
}
