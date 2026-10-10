using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddRoyaltyWriteOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoyaltyOriginCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoyaltyOriginName",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoyaltySituation",
                table: "WEIGHING_TICKETS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoyaltyTestCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoyaltyOriginCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoyaltyOriginName",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoyaltySituation",
                table: "STORAGE_TRANSACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoyaltyTestCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ControlsRoyalties",
                table: "ITEM_COMPLEMENTS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ROYALTY_WRITE_OFFS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WriteOffDate = table.Column<DateTime>(type: "DATE", nullable: false),
                    ItsId = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(18)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Situation = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
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
                    table.PrimaryKey("PK_ROYALTY_WRITE_OFFS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ROYALTY_WRITE_OFF_LINES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoyaltyWriteOffKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROYALTY_WRITE_OFF_LINES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ROYALTY_WRITE_OFF_LINES_ROYALTY_WRITE_OFFS_RoyaltyWriteOffKey",
                        column: x => x.RoyaltyWriteOffKey,
                        principalTable: "ROYALTY_WRITE_OFFS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ROYALTY_WRITE_OFF_LINES_STORAGE_TRANSACTIONS_StorageTransactionKey",
                        column: x => x.StorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ROYALTY_WRITE_OFF_LINES_RoyaltyWriteOffKey",
                table: "ROYALTY_WRITE_OFF_LINES",
                column: "RoyaltyWriteOffKey");

            migrationBuilder.CreateIndex(
                name: "IX_ROYALTY_WRITE_OFF_LINES_StorageTransactionKey",
                table: "ROYALTY_WRITE_OFF_LINES",
                column: "StorageTransactionKey")
                .Annotation("SqlServer:Include", new[] { "Weight" });

            migrationBuilder.CreateIndex(
                name: "IX_ROYALTY_WRITE_OFFS_ItsId",
                table: "ROYALTY_WRITE_OFFS",
                column: "ItsId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ROYALTY_WRITE_OFF_LINES");

            migrationBuilder.DropTable(
                name: "ROYALTY_WRITE_OFFS");

            migrationBuilder.DropColumn(
                name: "RoyaltyOriginCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "RoyaltyOriginName",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "RoyaltySituation",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "RoyaltyTestCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "RoyaltyOriginCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "RoyaltyOriginName",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "RoyaltySituation",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "RoyaltyTestCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ControlsRoyalties",
                table: "ITEM_COMPLEMENTS");
        }
    }
}
