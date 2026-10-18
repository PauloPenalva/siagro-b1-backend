using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class ReplaceUsageFunruralRatesWithContributionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RuralContributionMode",
                table: "USAGES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsRuralProducer",
                table: "BUSINESS_PARTNERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RuralPayrollOption",
                table: "BUSINESS_PARTNERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RuralProducerCpf",
                table: "BUSINESS_PARTNERS",
                type: "VARCHAR(11)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuralProducerType",
                table: "BUSINESS_PARTNERS",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RURAL_CONTRIBUTION_RATES",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    Scheme = table.Column<int>(type: "int", nullable: false),
                    InssRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    RatRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    SenarRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RURAL_CONTRIBUTION_RATES", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RURAL_CONTRIBUTION_RATES_Scheme_StartDate",
                table: "RURAL_CONTRIBUTION_RATES",
                columns: new[] { "Scheme", "StartDate" },
                unique: true);

            // Backfill: a natureza que cobrava INSS/RAT vira modo Full (1); só SENAR vira SenarOnly (2).
            // Precisa rodar ANTES de dropar as alíquotas abaixo.
            migrationBuilder.Sql(@"
UPDATE USAGES SET RuralContributionMode = CASE
  WHEN ISNULL(FunruralInssRate,0) > 0 OR ISNULL(FunruralRatRate,0) > 0 THEN 1
  WHEN ISNULL(FunruralSenarRate,0) > 0 THEN 2 ELSE 0 END;");

            // Seed idempotente das alíquotas vigentes desde 2000-01-01 (Scheme 1 = Standard/Comercialização, 2 = PayrollOption/Optante pela folha).
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM RURAL_CONTRIBUTION_RATES WHERE Scheme = 1 AND StartDate = '2000-01-01')
  INSERT INTO RURAL_CONTRIBUTION_RATES (StartDate, Scheme, InssRate, RatRate, SenarRate)
  VALUES ('2000-01-01', 1, 1.32, 0.11, 0.20);");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM RURAL_CONTRIBUTION_RATES WHERE Scheme = 2 AND StartDate = '2000-01-01')
  INSERT INTO RURAL_CONTRIBUTION_RATES (StartDate, Scheme, InssRate, RatRate, SenarRate)
  VALUES ('2000-01-01', 2, 0, 0, 0.20);");

            migrationBuilder.DropColumn(
                name: "FunruralInssRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "FunruralRatRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "FunruralSenarRate",
                table: "USAGES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RURAL_CONTRIBUTION_RATES");

            migrationBuilder.DropColumn(
                name: "RuralContributionMode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IsRuralProducer",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "RuralPayrollOption",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "RuralProducerCpf",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.DropColumn(
                name: "RuralProducerType",
                table: "BUSINESS_PARTNERS");

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralInssRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralRatRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FunruralSenarRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);
        }
    }
}
