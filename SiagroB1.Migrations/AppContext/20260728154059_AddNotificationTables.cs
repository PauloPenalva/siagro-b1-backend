using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddNotificationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NOTIFICATION_GROUPS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_GROUPS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION_OUTBOX_MESSAGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    DocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TriggeredBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_OUTBOX_MESSAGES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION_GROUP_MEMBERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Phone = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    PhoneE164 = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    WhatsAppJid = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_GROUP_MEMBERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_GROUP_MEMBERS_NOTIFICATION_GROUPS_NotificationGroupKey",
                        column: x => x.NotificationGroupKey,
                        principalTable: "NOTIFICATION_GROUPS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION_GROUP_SUBSCRIPTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_GROUP_SUBSCRIPTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_GROUP_SUBSCRIPTIONS_NOTIFICATION_GROUPS_NotificationGroupKey",
                        column: x => x.NotificationGroupKey,
                        principalTable: "NOTIFICATION_GROUPS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATION_DELIVERY_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OutboxMessageKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotificationGroupKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GroupName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    RecipientName = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    RecipientPhone = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    MessageText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATION_DELIVERY_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_NOTIFICATION_DELIVERY_LOGS_NOTIFICATION_OUTBOX_MESSAGES_OutboxMessageKey",
                        column: x => x.OutboxMessageKey,
                        principalTable: "NOTIFICATION_OUTBOX_MESSAGES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_DELIVERY_LOGS_OutboxMessageKey",
                table: "NOTIFICATION_DELIVERY_LOGS",
                column: "OutboxMessageKey");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_DELIVERY_LOGS_SentAt",
                table: "NOTIFICATION_DELIVERY_LOGS",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_DELIVERY_LOGS_Status",
                table: "NOTIFICATION_DELIVERY_LOGS",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_GROUP_MEMBERS_NotificationGroupKey",
                table: "NOTIFICATION_GROUP_MEMBERS",
                column: "NotificationGroupKey");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_GROUP_MEMBERS_NotificationGroupKey_PhoneE164",
                table: "NOTIFICATION_GROUP_MEMBERS",
                columns: new[] { "NotificationGroupKey", "PhoneE164" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_GROUP_SUBSCRIPTIONS_DocumentType_EventType",
                table: "NOTIFICATION_GROUP_SUBSCRIPTIONS",
                columns: new[] { "DocumentType", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_GROUP_SUBSCRIPTIONS_NotificationGroupKey_DocumentType_EventType",
                table: "NOTIFICATION_GROUP_SUBSCRIPTIONS",
                columns: new[] { "NotificationGroupKey", "DocumentType", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_GROUPS_Code",
                table: "NOTIFICATION_GROUPS",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_OUTBOX_MESSAGES_DocumentKey",
                table: "NOTIFICATION_OUTBOX_MESSAGES",
                column: "DocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATION_OUTBOX_MESSAGES_Status_OccurredAt",
                table: "NOTIFICATION_OUTBOX_MESSAGES",
                columns: new[] { "Status", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NOTIFICATION_DELIVERY_LOGS");

            migrationBuilder.DropTable(
                name: "NOTIFICATION_GROUP_MEMBERS");

            migrationBuilder.DropTable(
                name: "NOTIFICATION_GROUP_SUBSCRIPTIONS");

            migrationBuilder.DropTable(
                name: "NOTIFICATION_OUTBOX_MESSAGES");

            migrationBuilder.DropTable(
                name: "NOTIFICATION_GROUPS");
        }
    }
}
