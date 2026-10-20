using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddReceivedDfes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCH_DFE_SYNC_STATES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    LastNsu = table.Column<long>(type: "bigint", nullable: false),
                    MaxNsu = table.Column<long>(type: "bigint", nullable: false),
                    LastQueryAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextQueryAllowedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ScheduleTimes = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    LastScheduledRunAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCH_DFE_SYNC_STATES", x => x.BranchCode);
                });

            migrationBuilder.CreateTable(
                name: "RECEIVED_DFES",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: false),
                    Nsu = table.Column<long>(type: "bigint", nullable: false),
                    IssuerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    IssuerName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    IssueDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotalValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: true),
                    Situation = table.Column<int>(type: "int", nullable: false),
                    ManifestationStatus = table.Column<int>(type: "int", nullable: false),
                    ManifestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ManifestationProtocol = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    ManifestationJustification = table.Column<string>(type: "VARCHAR(255)", nullable: true),
                    ManifestationProcEventXml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    Xml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    XmlDownloadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PurchaseInvoiceDocEntry = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RECEIVED_DFES", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RECEIVED_DFES_BranchCode_AccessKey",
                table: "RECEIVED_DFES",
                columns: new[] { "BranchCode", "AccessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RECEIVED_DFES_IssueDate",
                table: "RECEIVED_DFES",
                column: "IssueDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BRANCH_DFE_SYNC_STATES");

            migrationBuilder.DropTable(
                name: "RECEIVED_DFES");
        }
    }
}
