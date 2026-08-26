using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateItemComplements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ITEM_COMPLEMENTS",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    CommercialUnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    CommercialFactor = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEM_COMPLEMENTS", x => x.ItemCode);
                });

            // ITEM_UNITS_OF_MEASURE nasceu em 25/08/2026 e so guardava o proposito Commercial;
            // ITEM_COMPLEMENTS a substitui com uma linha por item. Copiar ANTES de dropar, senao a
            // UoM comercial ja cadastrada some e o dialogo de faturamento volta a exibir KG.
            // Idempotente e tolerante a base virgem, onde a tabela antiga nao existe.
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'ITEM_UNITS_OF_MEASURE', N'U') IS NOT NULL
                    INSERT INTO ITEM_COMPLEMENTS (ItemCode, CommercialUnitOfMeasureCode, CommercialFactor)
                    SELECT u.ItemCode, u.UnitOfMeasureCode, u.Factor
                    FROM ITEM_UNITS_OF_MEASURE u
                    WHERE u.Purpose = 0
                      AND NOT EXISTS (SELECT 1 FROM ITEM_COMPLEMENTS c WHERE c.ItemCode = u.ItemCode);
            ");

            migrationBuilder.DropTable(
                name: "ITEM_UNITS_OF_MEASURE");
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ITEM_COMPLEMENTS");

            migrationBuilder.CreateTable(
                name: "ITEM_UNITS_OF_MEASURE",
                columns: table => new
                {
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Factor = table.Column<decimal>(type: "DECIMAL(18,6)", nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ITEM_UNITS_OF_MEASURE", x => new { x.ItemCode, x.Purpose });
                });
        }
    }
}
