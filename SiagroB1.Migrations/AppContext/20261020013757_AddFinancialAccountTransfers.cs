using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFinancialAccountTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FINANCIAL_ACCOUNT_TRANSFERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromAccountCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ToAccountCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    TransferDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Notes = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FINANCIAL_ACCOUNT_TRANSFERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNT_TRANSFERS_FINANCIAL_ACCOUNTS_FromAccountCode",
                        column: x => x.FromAccountCode,
                        principalTable: "FINANCIAL_ACCOUNTS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_FINANCIAL_ACCOUNT_TRANSFERS_FINANCIAL_ACCOUNTS_ToAccountCode",
                        column: x => x.ToAccountCode,
                        principalTable: "FINANCIAL_ACCOUNTS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_ENTRIES_TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                column: "TransferKey");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_TRANSFERS_FromAccountCode",
                table: "FINANCIAL_ACCOUNT_TRANSFERS",
                column: "FromAccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_TRANSFERS_ToAccountCode",
                table: "FINANCIAL_ACCOUNT_TRANSFERS",
                column: "ToAccountCode");

            migrationBuilder.CreateIndex(
                name: "IX_FINANCIAL_ACCOUNT_TRANSFERS_TransferDate",
                table: "FINANCIAL_ACCOUNT_TRANSFERS",
                column: "TransferDate");

            migrationBuilder.AddForeignKey(
                name: "FK_FINANCIAL_ACCOUNT_ENTRIES_FINANCIAL_ACCOUNT_TRANSFERS_TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES",
                column: "TransferKey",
                principalTable: "FINANCIAL_ACCOUNT_TRANSFERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FINANCIAL_ACCOUNT_ENTRIES_FINANCIAL_ACCOUNT_TRANSFERS_TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropTable(
                name: "FINANCIAL_ACCOUNT_TRANSFERS");

            migrationBuilder.DropIndex(
                name: "IX_FINANCIAL_ACCOUNT_ENTRIES_TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES");

            migrationBuilder.DropColumn(
                name: "TransferKey",
                table: "FINANCIAL_ACCOUNT_ENTRIES");
        }
    }
}
