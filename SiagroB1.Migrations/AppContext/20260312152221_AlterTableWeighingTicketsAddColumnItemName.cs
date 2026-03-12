using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableWeighingTicketsAddColumnItemName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(200)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardName",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "WEIGHING_TICKETS");
        }
    }
}
