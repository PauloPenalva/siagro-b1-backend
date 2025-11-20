using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterColumnSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Sequence",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(3)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(3)",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Sequence",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(3)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(3)");
        }
    }
}
