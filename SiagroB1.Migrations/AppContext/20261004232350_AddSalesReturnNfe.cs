using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesReturnNfe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReturnUsageCode",
                table: "USAGES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NfeItemNumber",
                table: "SALES_INVOICES_ITEMS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsNfeReturn",
                table: "SALES_INVOICES",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_USAGES_ReturnUsageCode",
                table: "USAGES",
                column: "ReturnUsageCode");

            migrationBuilder.AddForeignKey(
                name: "FK_USAGES_USAGES_ReturnUsageCode",
                table: "USAGES",
                column: "ReturnUsageCode",
                principalTable: "USAGES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_USAGES_USAGES_ReturnUsageCode",
                table: "USAGES");

            migrationBuilder.DropIndex(
                name: "IX_USAGES_ReturnUsageCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "ReturnUsageCode",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "NfeItemNumber",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "IsNfeReturn",
                table: "SALES_INVOICES");
        }
    }
}
