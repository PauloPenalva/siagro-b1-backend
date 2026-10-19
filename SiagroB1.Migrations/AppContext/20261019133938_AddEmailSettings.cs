using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EMAIL_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false),
                    Host = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    UseStartTls = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    PasswordEncrypted = table.Column<byte[]>(type: "VARBINARY(512)", nullable: true),
                    FromAddress = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    FromName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    AutoSendEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMAIL_SETTINGS", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EMAIL_SETTINGS");
        }
    }
}
