using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateTableBusinessPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BUSINESS_PARTNERS",
                columns: table => new
                {
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    AliasName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    CardType = table.Column<string>(type: "VARCHAR(1)", nullable: true),
                    U_YKT_CNPJ_CPF = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    QryGroup23 = table.Column<string>(type: "VARCHAR(1)", nullable: true),
                    Free_Text = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUSINESS_PARTNERS", x => x.CardCode);
                });

            migrationBuilder.CreateTable(
                name: "BUSINESS_PARTNERS_ADDRESSES",
                columns: table => new
                {
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    Address = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    AdresType = table.Column<string>(type: "VARCHAR(1)", nullable: false),
                    Street = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Block = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ZipCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    City = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BUSINESS_PARTNERS_ADDRESSES", x => new { x.CardCode, x.Address, x.AdresType });
                    table.ForeignKey(
                        name: "FK_BUSINESS_PARTNERS_ADDRESSES_BUSINESS_PARTNERS_CardCode",
                        column: x => x.CardCode,
                        principalTable: "BUSINESS_PARTNERS",
                        principalColumn: "CardCode");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BUSINESS_PARTNERS_ADDRESSES");

            migrationBuilder.DropTable(
                name: "BUSINESS_PARTNERS");
        }
    }
}
