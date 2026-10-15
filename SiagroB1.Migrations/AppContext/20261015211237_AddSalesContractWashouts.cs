using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>Washout do contrato de venda (spec 2026-10-15): tabela filha + volumes lavados persistidos.</summary>
    /// <inheritdoc />
    public partial class AddSalesContractWashouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "WashedOutUnfixedVolume",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WashedOutVolume",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_WASHOUTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PriceFixationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FixedVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnfixedVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ContractPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    MarketPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    PenaltyAmount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovalComments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ReversalReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    FinancialDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
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
                    table.PrimaryKey("PK_SALES_CONTRACTS_WASHOUTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_WASHOUTS_FINANCIAL_DOCUMENTS_FinancialDocumentKey",
                        column: x => x.FinancialDocumentKey,
                        principalTable: "FINANCIAL_DOCUMENTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_WASHOUTS_SALES_CONTRACTS_PRICE_FIXATIONS_PriceFixationKey",
                        column: x => x.PriceFixationKey,
                        principalTable: "SALES_CONTRACTS_PRICE_FIXATIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_WASHOUTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_WASHOUTS_FinancialDocumentKey",
                table: "SALES_CONTRACTS_WASHOUTS",
                column: "FinancialDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_WASHOUTS_PriceFixationKey",
                table: "SALES_CONTRACTS_WASHOUTS",
                column: "PriceFixationKey");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_WASHOUTS_SalesContractKey_Sequence",
                table: "SALES_CONTRACTS_WASHOUTS",
                columns: new[] { "SalesContractKey", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_WASHOUTS");

            migrationBuilder.DropColumn(
                name: "WashedOutUnfixedVolume",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "WashedOutVolume",
                table: "SALES_CONTRACTS");
        }
    }
}
