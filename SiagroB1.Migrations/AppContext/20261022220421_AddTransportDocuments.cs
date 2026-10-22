using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTransportDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CTE_NUMBERS_TO_VOID",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(255)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    VoidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VoidProtocol = table.Column<string>(type: "VARCHAR(20)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CTE_NUMBERS_TO_VOID", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: true),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: true),
                    RandomCode = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<int>(type: "int", nullable: true),
                    UsageCode = table.Column<int>(type: "int", nullable: true),
                    Cfop = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    NatureText = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    TakerRole = table.Column<int>(type: "int", nullable: false),
                    StartMunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    StartState = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    EndMunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    EndState = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    PredominantProduct = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    CargoValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ServiceTotal = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    AmountToReceive = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IcmsCst = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    IcmsBase = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IcmsRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IcmsReduction = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IcmsValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IbsCbsCst = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    IbsCbsClassCode = table.Column<string>(type: "VARCHAR(6)", nullable: true),
                    IbsCbsBase = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    CbsRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    CbsRateReduction = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    CbsValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IbsStateRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IbsMunicipalRate = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IbsRateReduction = table.Column<decimal>(type: "DECIMAL(7,4)", nullable: false),
                    IbsStateValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    IbsMunicipalValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Rntrc = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    TruckCombinationCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    TractionPlate = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    TrailerPlates = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    VehicleSnapshotJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    DriverCpf = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    DriverName = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    InsuranceResponsible = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    InsurerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    InsurerName = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    PolicyNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    Tracked = table.Column<bool>(type: "bit", nullable: false),
                    OwnTransport = table.Column<bool>(type: "bit", nullable: false),
                    Observation = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    CostCenterCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AccountingItemCode = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    DistributionRuleCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    Protocol = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusCode = table.Column<string>(type: "VARCHAR(5)", nullable: true),
                    StatusReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CancellationProtocol = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "VARCHAR(255)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENTS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_COMPONENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_COMPONENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_COMPONENTS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_CORRECTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ItemsJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    Protocol = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_CORRECTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_CORRECTIONS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_NFE_KEYS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_NFE_KEYS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_NFE_KEYS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_PARTIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: true),
                    Name = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    StateRegistration = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    IndIE = table.Column<int>(type: "int", nullable: false),
                    Street = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Number = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Complement = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    Neighborhood = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    MunicipalityCode = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    MunicipalityName = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    State = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    ZipCode = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    Phone = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Email = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_PARTIES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_PARTIES_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_QUANTITIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitCode = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    MeasureType = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(15,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_QUANTITIES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_QUANTITIES_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_XMLS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Xml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_XMLS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_XMLS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CTE_NUMBERS_TO_VOID_BranchCode_Series_Number",
                table: "CTE_NUMBERS_TO_VOID",
                columns: new[] { "BranchCode", "Series", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_COMPONENTS_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_COMPONENTS",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_CORRECTIONS_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_CORRECTIONS",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_NFE_KEYS_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_NFE_KEYS",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_PARTIES_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_PARTIES",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_QUANTITIES_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_QUANTITIES",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_XMLS_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_XMLS",
                column: "TransportDocumentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENTS_AccessKey",
                table: "TRANSPORT_DOCUMENTS",
                column: "AccessKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENTS_BranchCode_Series_Number",
                table: "TRANSPORT_DOCUMENTS",
                columns: new[] { "BranchCode", "Series", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CTE_NUMBERS_TO_VOID");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_COMPONENTS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_CORRECTIONS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_NFE_KEYS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_PARTIES");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_QUANTITIES");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_XMLS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENTS");
        }
    }
}
