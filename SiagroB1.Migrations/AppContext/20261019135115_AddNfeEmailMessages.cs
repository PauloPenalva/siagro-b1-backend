using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddNfeEmailMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NFE_EMAIL_MESSAGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    DocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Recipients = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NFE_EMAIL_MESSAGES", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NFE_EMAIL_MESSAGES_DocumentType_DocumentKey_EventType",
                table: "NFE_EMAIL_MESSAGES",
                columns: new[] { "DocumentType", "DocumentKey", "EventType" },
                unique: true,
                filter: "[Trigger] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_NFE_EMAIL_MESSAGES_Status_CreatedAt",
                table: "NFE_EMAIL_MESSAGES",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NFE_EMAIL_MESSAGES");
        }
    }
}
