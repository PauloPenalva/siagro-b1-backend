using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddReinf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuralJudicialProcessNumber",
                table: "USAGES",
                type: "VARCHAR(21)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuralJudicialSuspensionCode",
                table: "USAGES",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "REINF_BATCHES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxIdRoot = table.Column<string>(type: "VARCHAR(8)", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    Protocol = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestXml = table.Column<string>(type: "VARCHAR(MAX)", nullable: false),
                    ResponseXml = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    Error = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REINF_BATCHES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "REINF_EVENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    TaxIdRoot = table.Column<string>(type: "VARCHAR(8)", nullable: false),
                    Period = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    EstablishmentTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    ProducerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    EventId = table.Column<string>(type: "VARCHAR(36)", nullable: false),
                    RectificationIndicator = table.Column<int>(type: "int", nullable: false),
                    PreviousReceipt = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Xml = table.Column<string>(type: "VARCHAR(MAX)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Receipt = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Occurrences = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    GrossValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    InssValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    RatValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    SenarValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ContentHash = table.Column<string>(type: "VARCHAR(64)", nullable: true),
                    BatchKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REINF_EVENTS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "REINF_PERIODS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxIdRoot = table.Column<string>(type: "VARCHAR(8)", nullable: false),
                    Period = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosingReceipt = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REINF_PERIODS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "REINF_SETTINGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxIdRoot = table.Column<string>(type: "VARCHAR(8)", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    TaxClassification = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    KeepsBookkeeping = table.Column<bool>(type: "bit", nullable: false),
                    PayrollRelief = table.Column<bool>(type: "bit", nullable: false),
                    FineExemptionAgreement = table.Column<bool>(type: "bit", nullable: false),
                    CompanySituation = table.Column<int>(type: "int", nullable: true),
                    ContactName = table.Column<string>(type: "VARCHAR(70)", nullable: false),
                    ContactCpf = table.Column<string>(type: "VARCHAR(11)", nullable: false),
                    ContactPhone = table.Column<string>(type: "VARCHAR(13)", nullable: true),
                    ContactEmail = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    ValidFrom = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    ClosingResponsibleName = table.Column<string>(type: "VARCHAR(70)", nullable: true),
                    ClosingResponsibleCpf = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    ClosingResponsiblePhone = table.Column<string>(type: "VARCHAR(13)", nullable: true),
                    ClosingResponsibleEmail = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    RegistrationStatus = table.Column<int>(type: "int", nullable: false),
                    RegistrationReceipt = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REINF_SETTINGS", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_REINF_BATCHES_Protocol",
                table: "REINF_BATCHES",
                column: "Protocol");

            migrationBuilder.CreateIndex(
                name: "IX_REINF_EVENTS_BatchKey",
                table: "REINF_EVENTS",
                column: "BatchKey");

            migrationBuilder.CreateIndex(
                name: "IX_REINF_EVENTS_TaxIdRoot_Period_Type",
                table: "REINF_EVENTS",
                columns: new[] { "TaxIdRoot", "Period", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_REINF_PERIODS_TaxIdRoot_Period",
                table: "REINF_PERIODS",
                columns: new[] { "TaxIdRoot", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REINF_SETTINGS_TaxIdRoot",
                table: "REINF_SETTINGS",
                column: "TaxIdRoot",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "REINF_BATCHES");

            migrationBuilder.DropTable(
                name: "REINF_EVENTS");

            migrationBuilder.DropTable(
                name: "REINF_PERIODS");

            migrationBuilder.DropTable(
                name: "REINF_SETTINGS");

            migrationBuilder.DropColumn(
                name: "RuralJudicialProcessNumber",
                table: "USAGES");

            migrationBuilder.DropColumn(
                name: "RuralJudicialSuspensionCode",
                table: "USAGES");
        }
    }
}
