using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableWeighingTicketAddColumnWeighUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstWeighUsername",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondWeighUsername",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(200)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstWeighUsername",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "SecondWeighUsername",
                table: "WEIGHING_TICKETS");
        }
    }
}
