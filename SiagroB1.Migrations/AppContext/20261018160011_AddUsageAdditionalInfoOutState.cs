using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddUsageAdditionalInfoOutState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultAdditionalInfoOutState",
                table: "USAGES",
                type: "VARCHAR(2000)",
                nullable: true);

            // Até aqui o texto único valia para as duas operações: ele passa a valer também fora do
            // estado, para nenhuma natureza perder o texto sem ninguém ver. A natureza cujo texto só
            // serve dentro do estado (ex.: diferimento) precisa ter o "fora do estado" revisto no cadastro.
            migrationBuilder.Sql(
                "UPDATE USAGES SET DefaultAdditionalInfoOutState = DefaultAdditionalInfo " +
                "WHERE DefaultAdditionalInfo IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultAdditionalInfoOutState",
                table: "USAGES");
        }
    }
}
