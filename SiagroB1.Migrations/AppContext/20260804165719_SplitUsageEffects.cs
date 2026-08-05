using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Separa o EFEITO de negócio da IDENTIDADE fiscal da natureza de operação.
    ///
    /// É o que destrava o modo SAPB1: lá a identidade vem de OUSG e o Siagro não a escreve,
    /// então enquanto o efeito morava na mesma linha não havia onde gravá-lo — as naturezas
    /// chegavam todas sem efeito e nenhum documento mexia no contrato. Agora o efeito mora em
    /// USAGE_EFFECTS, chaveado pelo MESMO int dos dois mundos (USAGES.Code ou OUSG.ID).
    ///
    /// A ORDEM importa: o EF gerou os DROPs antes do CreateTable, o que jogaria fora a
    /// configuração já existente (inclusive a natureza semente, que é Consume e padrão).
    /// Aqui é criar, COPIAR e só então dropar.
    /// </summary>
    public partial class SplitUsageEffects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USAGE_EFFECTS",
                columns: table => new
                {
                    UsageCode = table.Column<int>(type: "int", nullable: false),
                    ContractBalanceEffect = table.Column<int>(type: "int", nullable: false),
                    ContractValueEffect = table.Column<int>(type: "int", nullable: false),
                    RequiresContract = table.Column<bool>(type: "bit", nullable: false),
                    RequiresQuantity = table.Column<bool>(type: "bit", nullable: false),
                    RequiresWeight = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USAGE_EFFECTS", x => x.UsageCode);
                });

            // Toda natureza local já cadastrada nasce COM efeito configurado — inclusive a
            // semente do faturamento de romaneio. Sem esta cópia elas passariam a ser
            // recusadas pelo guard de "natureza sem efeito configurado".
            migrationBuilder.Sql(@"
                INSERT INTO USAGE_EFFECTS
                    (UsageCode, ContractBalanceEffect, ContractValueEffect,
                     RequiresContract, RequiresQuantity, RequiresWeight, IsDefault)
                SELECT u.Code, u.ContractBalanceEffect, u.ContractValueEffect,
                       u.RequiresContract, u.RequiresQuantity, u.RequiresWeight, u.IsDefault
                  FROM USAGES u
                 WHERE NOT EXISTS (SELECT 1 FROM USAGE_EFFECTS e WHERE e.UsageCode = u.Code);
            ");

            migrationBuilder.DropColumn(
                name: "ContractBalanceEffect",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "ContractValueEffect",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "RequiresContract",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "RequiresQuantity",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "RequiresWeight",
                table: "USAGES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContractBalanceEffect",
                table: "USAGES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContractValueEffect",
                table: "USAGES",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresContract",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresQuantity",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresWeight",
                table: "USAGES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Devolve o efeito para a natureza local. O que estiver configurado para natureza
            // do SAP não tem para onde voltar — reverter é com perda nesse caso.
            migrationBuilder.Sql(@"
                UPDATE u
                   SET u.ContractBalanceEffect = e.ContractBalanceEffect,
                       u.ContractValueEffect   = e.ContractValueEffect,
                       u.RequiresContract      = e.RequiresContract,
                       u.RequiresQuantity      = e.RequiresQuantity,
                       u.RequiresWeight        = e.RequiresWeight,
                       u.IsDefault             = e.IsDefault
                  FROM USAGES u
                  JOIN USAGE_EFFECTS e ON e.UsageCode = u.Code;
            ");

            migrationBuilder.DropTable(
                name: "USAGE_EFFECTS");
        }
    }
}
