using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreateUsagesAndSalesInvoiceUsageCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UsageCode",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "USAGES",
                columns: table => new
                {
                    Code = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    CfopOutgoingInState = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    CfopOutgoingOutState = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    ContractBalanceEffect = table.Column<int>(type: "int", nullable: false),
                    ContractValueEffect = table.Column<int>(type: "int", nullable: false),
                    RequiresContract = table.Column<bool>(type: "bit", nullable: false),
                    RequiresQuantity = table.Column<bool>(type: "bit", nullable: false),
                    RequiresWeight = table.Column<bool>(type: "bit", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USAGES", x => x.Code);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USAGES");

            migrationBuilder.DropColumn(
                name: "UsageCode",
                table: "SALES_INVOICES");
        }
    }
}
