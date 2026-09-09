using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadCommentsAndChangeLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOADS_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOADS_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_CHANGE_LOGS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOADS_COMMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommentedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CommentText = table.Column<string>(type: "VARCHAR(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOADS_COMMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_COMMENTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_CHANGE_LOGS_ShipmentLoadKey",
                table: "SHIPMENT_LOADS_CHANGE_LOGS",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_COMMENTS_ShipmentLoadKey",
                table: "SHIPMENT_LOADS_COMMENTS",
                column: "ShipmentLoadKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPMENT_LOADS_CHANGE_LOGS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOADS_COMMENTS");
        }
    }
}
