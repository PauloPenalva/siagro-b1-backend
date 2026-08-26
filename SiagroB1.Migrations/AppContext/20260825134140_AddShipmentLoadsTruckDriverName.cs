using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadsTruckDriverName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TruckDriverName",
                table: "SHIPMENT_LOADS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE SL
                SET SL.TruckDriverName = TD.Name
                FROM SHIPMENT_LOADS SL
                INNER JOIN TRUCK_DRIVERS TD ON TD.Code = SL.TruckDriverCode
                WHERE SL.TruckDriverCode IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TruckDriverName",
                table: "SHIPMENT_LOADS");
        }
    }
}
