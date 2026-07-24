using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesContractChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_CHANGE_LOGS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_CHANGE_LOGS_SalesContractKey",
                table: "SALES_CONTRACTS_CHANGE_LOGS",
                column: "SalesContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_CHANGE_LOGS");
        }
    }
}
