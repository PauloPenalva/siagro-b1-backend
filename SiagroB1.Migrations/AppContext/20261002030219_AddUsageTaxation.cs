using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddUsageTaxation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CbsRateReduction",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CfopIncomingInState",
                table: "USAGES",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CfopIncomingOutState",
                table: "USAGES",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CofinsCst",
                table: "USAGES",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CofinsRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreatesFinancialDocument",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DefaultAdditionalInfo",
                table: "USAGES",
                type: "VARCHAR(2000)",
                nullable: true);

            // Default 1 (Saída), não 0: as naturezas existentes são todas de saída, e 0 não é
            // membro de UsageDirection — a serialização OData estouraria ao ler a lista.
            migrationBuilder.AddColumn<int>(
                name: "Direction",
                table: "USAGES",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeIcmsFromPisCofinsBase",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsClassCode",
                table: "USAGES",
                type: "VARCHAR(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IbsCbsCst",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IbsRateReduction",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsInStateBaseReduction",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsInStateBenefitCode",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsInStateCsosn",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsInStateCst",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsInStateDeferral",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsInStateRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsOutStateBaseReduction",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsOutStateBenefitCode",
                table: "USAGES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsOutStateCsosn",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcmsOutStateCst",
                table: "USAGES",
                type: "VARCHAR(3)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IcmsOutStateDeferral",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceOperationText",
                table: "USAGES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MovesFiscalInventory",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PisCst",
                table: "USAGES",
                type: "VARCHAR(2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PisRate",
                table: "USAGES",
                type: "DECIMAL(7,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CbsRateReduction",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "CfopIncomingInState",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "CfopIncomingOutState",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "CofinsCst",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "CofinsRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "CreatesFinancialDocument",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "DefaultAdditionalInfo",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "ExcludeIcmsFromPisCofinsBase",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IbsCbsClassCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IbsCbsCst",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IbsRateReduction",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateBaseReduction",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateBenefitCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateCsosn",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateCst",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateDeferral",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsInStateRate",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsOutStateBaseReduction",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsOutStateBenefitCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsOutStateCsosn",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsOutStateCst",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IcmsOutStateDeferral",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "InvoiceOperationText",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "MovesFiscalInventory",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "PisCst",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "PisRate",
                table: "USAGES");
        }
    }
}
