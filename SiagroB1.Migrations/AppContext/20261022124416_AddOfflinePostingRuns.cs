using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddOfflinePostingRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OFFLINE_POSTING_RUNS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DateFrom = table.Column<DateTime>(type: "DATE", nullable: false),
                    DateTo = table.Column<DateTime>(type: "DATE", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Modules = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Simulation = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailureMessage = table.Column<string>(type: "VARCHAR(2000)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OFFLINE_POSTING_RUNS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OFFLINE_POSTING_RUN_ITEMS",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    EventDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    DocumentType = table.Column<string>(type: "VARCHAR(40)", nullable: false),
                    DocumentKey = table.Column<string>(type: "VARCHAR(40)", nullable: false),
                    DocumentNumber = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Codes = table.Column<string>(type: "VARCHAR(40)", nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    JournalEntryDocEntries = table.Column<string>(type: "VARCHAR(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OFFLINE_POSTING_RUN_ITEMS", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OFFLINE_POSTING_RUN_ITEMS_OFFLINE_POSTING_RUNS_RunKey",
                        column: x => x.RunKey,
                        principalTable: "OFFLINE_POSTING_RUNS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OFFLINE_POSTING_RUN_ITEMS_RunKey",
                table: "OFFLINE_POSTING_RUN_ITEMS",
                column: "RunKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OFFLINE_POSTING_RUN_ITEMS");

            migrationBuilder.DropTable(
                name: "OFFLINE_POSTING_RUNS");
        }
    }
}
