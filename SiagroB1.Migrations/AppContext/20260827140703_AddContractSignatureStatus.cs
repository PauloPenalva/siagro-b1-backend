using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddContractSignatureStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SignatureStatus",
                table: "SALES_CONTRACTS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SignatureStatus",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureStatus",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "SignatureStatus",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
