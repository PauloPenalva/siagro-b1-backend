using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterUserTruckScalesUniqueIndexIncludeScaleCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USER_TRUCK_SCALES_Username_Purpose",
                table: "USER_TRUCK_SCALES");

            migrationBuilder.CreateIndex(
                name: "IX_USER_TRUCK_SCALES_Username_TruckScaleCode_Purpose",
                table: "USER_TRUCK_SCALES",
                columns: new[] { "Username", "TruckScaleCode", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USER_TRUCK_SCALES_Username_TruckScaleCode_Purpose",
                table: "USER_TRUCK_SCALES");

            migrationBuilder.CreateIndex(
                name: "IX_USER_TRUCK_SCALES_Username_Purpose",
                table: "USER_TRUCK_SCALES",
                columns: new[] { "Username", "Purpose" },
                unique: true);
        }
    }
}
