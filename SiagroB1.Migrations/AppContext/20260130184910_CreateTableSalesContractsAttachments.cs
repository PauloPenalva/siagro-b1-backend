using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableSalesContractsAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_ATTACHMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_ATTACHMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_ATTACHMENTS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_ATTACHMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_ATTACHMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_ATTACHMENTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_ATTACHMENTS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ATTACHMENTS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_ATTACHMENTS_SalesContractKey",
                table: "SALES_CONTRACTS_ATTACHMENTS",
                column: "SalesContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_ATTACHMENTS");

            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_ATTACHMENTS");
        }
    }
}
