using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesContractPriceFixation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FixedVolume",
                table: "SALES_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FixationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinancialDueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentDetails = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    FreightCost = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    FixationVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    FixationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ApprovalComments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
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
                    table.PrimaryKey("PK_SALES_CONTRACTS_PRICE_FIXATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_PRICE_FIXATIONS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_PRICE_FIXATIONS_SalesContractKey",
                table: "SALES_CONTRACTS_PRICE_FIXATIONS",
                column: "SalesContractKey");

            // Backfill de paridade: TotalPrice do contrato de venda passa a derivar das
            // fixações Confirmed. Contratos de preço fixo (Type = 0) existentes não têm
            // fixação e ficariam com TotalPrice = 0. Espelha o preço já acordado numa fixação
            // Confirmed cobrindo todo o volume — mesmo papel de ConfirmFixedContractAutoFixations
            // no lado da compra. PAF (Type = 1) e contratos sem preço/volume não recebem fixação.
            migrationBuilder.Sql(@"
INSERT INTO SALES_CONTRACTS_PRICE_FIXATIONS
    ([Key], SalesContractKey, FixationDate, FreightCost, FixationVolume, FixationPrice,
     Status, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
SELECT NEWID(), c.[Key], ISNULL(c.CreationDate, GETDATE()), 0, c.TotalVolume, c.Price,
       1, GETDATE(), 'migration', GETDATE(), 'migration'
FROM SALES_CONTRACTS c
WHERE c.[Type] = 0
  AND c.Price > 0
  AND c.TotalVolume > 0
  AND NOT EXISTS (SELECT 1 FROM SALES_CONTRACTS_PRICE_FIXATIONS f
                  WHERE f.SalesContractKey = c.[Key]);");

            // FixedVolume persistido reflete o volume reservado pela fixação recém-criada.
            migrationBuilder.Sql(@"
UPDATE c SET c.FixedVolume = c.TotalVolume
FROM SALES_CONTRACTS c
WHERE c.[Type] = 0 AND c.Price > 0 AND c.TotalVolume > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "FixedVolume",
                table: "SALES_CONTRACTS");
        }
    }
}
