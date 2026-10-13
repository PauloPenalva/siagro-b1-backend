using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFreightCalculationMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FreightCalculationMethod",
                table: "WEIGHING_TICKETS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightUnitPrice",
                table: "WEIGHING_TICKETS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DefaultFreightCalculationMethod",
                table: "SYSTEM_SETUP",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FreightCalculationMethod",
                table: "STORAGE_TRANSACTIONS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "FreightUnitPrice",
                table: "STORAGE_TRANSACTIONS",
                type: "DECIMAL(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Até aqui o FreightPrice era digitado como TOTAL. Todo documento existente vira Fixo
            // (defaultValue 0 acima) com unitário = total: a gravação passa a recalcular o total a
            // partir do unitário, e sem isto editar um romaneio ou ticket antigo zeraria o frete.
            // EXEC: o UPDATE usa colunas criadas neste lote — num script idempotente o SQL Server
            // compila o lote inteiro antes do ALTER e recusaria a coluna.
            migrationBuilder.Sql(
                "EXEC('UPDATE STORAGE_TRANSACTIONS SET FreightUnitPrice = FreightPrice WHERE FreightPrice <> 0')");
            migrationBuilder.Sql(
                "EXEC('UPDATE WEIGHING_TICKETS SET FreightUnitPrice = FreightPrice WHERE FreightPrice <> 0')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FreightCalculationMethod",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "FreightUnitPrice",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "DefaultFreightCalculationMethod",
                table: "SYSTEM_SETUP");

            migrationBuilder.DropColumn(
                name: "FreightCalculationMethod",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "FreightUnitPrice",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
