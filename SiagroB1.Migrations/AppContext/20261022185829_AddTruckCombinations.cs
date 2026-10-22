using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTruckCombinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TRUCK_COMBINATIONS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TractionTruckCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    DefaultDriverCode = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRUCK_COMBINATIONS", x => x.Code);
                    table.ForeignKey(
                        name: "FK_TRUCK_COMBINATIONS_TRUCKS_TractionTruckCode",
                        column: x => x.TractionTruckCode,
                        principalTable: "TRUCKS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_TRUCK_COMBINATIONS_TRUCK_DRIVERS_DefaultDriverCode",
                        column: x => x.DefaultDriverCode,
                        principalTable: "TRUCK_DRIVERS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "TRUCK_COMBINATION_TRAILERS",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CombinationCode = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    TrailerTruckCode = table.Column<string>(type: "VARCHAR(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRUCK_COMBINATION_TRAILERS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TRUCK_COMBINATION_TRAILERS_TRUCKS_TrailerTruckCode",
                        column: x => x.TrailerTruckCode,
                        principalTable: "TRUCKS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_TRUCK_COMBINATION_TRAILERS_TRUCK_COMBINATIONS_CombinationCode",
                        column: x => x.CombinationCode,
                        principalTable: "TRUCK_COMBINATIONS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_COMBINATION_TRAILERS_CombinationCode_TrailerTruckCode",
                table: "TRUCK_COMBINATION_TRAILERS",
                columns: new[] { "CombinationCode", "TrailerTruckCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_COMBINATION_TRAILERS_TrailerTruckCode",
                table: "TRUCK_COMBINATION_TRAILERS",
                column: "TrailerTruckCode");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_COMBINATIONS_DefaultDriverCode",
                table: "TRUCK_COMBINATIONS",
                column: "DefaultDriverCode");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_COMBINATIONS_TractionTruckCode",
                table: "TRUCK_COMBINATIONS",
                column: "TractionTruckCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRUCK_COMBINATION_TRAILERS");

            migrationBuilder.DropTable(
                name: "TRUCK_COMBINATIONS");
        }
    }
}
