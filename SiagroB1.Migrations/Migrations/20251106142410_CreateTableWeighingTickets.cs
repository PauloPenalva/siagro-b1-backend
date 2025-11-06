using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableWeighingTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WEIGHTING_TICKETS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Time = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    TruckKey = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    TruckDriverKey = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    FirstWeighValue = table.Column<int>(type: "int", nullable: false),
                    FirstWeighDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SecondWeighValue = table.Column<int>(type: "int", nullable: false),
                    SecondWeighDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrossWeight = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WEIGHTING_TICKETS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "QUALITY_INSPECTIONS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    WeighingTicketKey = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    QualityAttribKey = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUALITY_INSPECTIONS", x => new { x.BranchKey, x.WeighingTicketKey, x.QualityAttribKey });
                    table.ForeignKey(
                        name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribKey",
                        column: x => x.QualityAttribKey,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QUALITY_INSPECTIONS_WEIGHTING_TICKETS_WeighingTicketKey",
                        column: x => x.WeighingTicketKey,
                        principalTable: "WEIGHTING_TICKETS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_INSPECTIONS_QualityAttribKey",
                table: "QUALITY_INSPECTIONS",
                column: "QualityAttribKey");

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_INSPECTIONS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS",
                column: "WeighingTicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_BranchKey",
                table: "WEIGHTING_TICKETS",
                column: "BranchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QUALITY_INSPECTIONS");

            migrationBuilder.DropTable(
                name: "WEIGHTING_TICKETS");
        }
    }
}
