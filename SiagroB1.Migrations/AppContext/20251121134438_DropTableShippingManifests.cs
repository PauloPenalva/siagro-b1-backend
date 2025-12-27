#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class DropTableShippingManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPPING_MANIFESTS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHIPPING_MANIFESTS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    GrossWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ManifestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ManifestNumber = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    ShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TareWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPPING_MANIFESTS", x => x.Key);
                });
        }
    }
}
