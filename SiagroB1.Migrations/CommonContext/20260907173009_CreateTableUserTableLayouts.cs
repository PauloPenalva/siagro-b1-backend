using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class CreateTableUserTableLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "USER_TABLE_LAYOUTS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "VARCHAR(50)", maxLength: 50, nullable: false),
                    TableKey = table.Column<string>(type: "VARCHAR(200)", maxLength: 200, nullable: false),
                    LayoutJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_TABLE_LAYOUTS", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_USER_TABLE_LAYOUTS_Username_TableKey",
                table: "USER_TABLE_LAYOUTS",
                columns: new[] { "Username", "TableKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "USER_TABLE_LAYOUTS");
        }
    }
}
