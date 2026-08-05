using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Natureza padrão do faturamento de romaneio. O fluxo de romaneio não escolhe natureza
    /// em tela; sem essa flag o guard de obrigatoriedade quebraria o caminho principal.
    ///
    /// A semente criada na migration anterior é promovida a padrão aqui — é a mesma que o
    /// backfill aplicou aos documentos existentes, então o comportamento continua o de antes.
    /// </summary>
    public partial class AddUsageIsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(@"
                UPDATE USAGES SET IsDefault = 1 WHERE Name = 'Venda de grãos';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "USAGES");
        }
    }
}
