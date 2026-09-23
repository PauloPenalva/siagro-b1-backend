using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadIsDischarged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDischarged",
                table: "SHIPMENT_LOADS");
        }
    }
}
