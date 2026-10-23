using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddMdfeDriverInclusion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AddedByEvent",
                table: "TRANSPORT_MANIFEST_DRIVERS",
                newName: "IncludedByEvent");

            migrationBuilder.AddColumn<int>(
                name: "EventSequence",
                table: "TRANSPORT_MANIFEST_DRIVERS",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventSequence",
                table: "TRANSPORT_MANIFEST_DRIVERS");

            migrationBuilder.RenameColumn(
                name: "IncludedByEvent",
                table: "TRANSPORT_MANIFEST_DRIVERS",
                newName: "AddedByEvent");
        }
    }
}
