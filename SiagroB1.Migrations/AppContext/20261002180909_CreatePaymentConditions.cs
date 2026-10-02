using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreatePaymentConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentConditionCode",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PAYMENT_CONDITIONS",
                columns: table => new
                {
                    Code = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Days = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    StartRule = table.Column<int>(type: "int", nullable: false),
                    PaymentMeans = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENT_CONDITIONS", x => x.Code);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PAYMENT_CONDITIONS");

            migrationBuilder.DropColumn(
                name: "PaymentConditionCode",
                table: "SALES_INVOICES");
        }
    }
}
