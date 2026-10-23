using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddTransportManifests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFESTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Series = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: true),
                    RandomCode = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Environment = table.Column<int>(type: "int", nullable: true),
                    UfStart = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    UfEnd = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    TractionPlate = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    DriverCpf = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    DriverName = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    CargoType = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    ProductDescription = table.Column<string>(type: "VARCHAR(120)", nullable: true),
                    Ncm = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    LoadingZip = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    UnloadingZip = table.Column<string>(type: "VARCHAR(8)", nullable: true),
                    CteCount = table.Column<int>(type: "int", nullable: false),
                    CargoValue = table.Column<decimal>(type: "DECIMAL(15,2)", nullable: false),
                    WeightUnit = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    GrossWeight = table.Column<decimal>(type: "DECIMAL(15,4)", nullable: false),
                    CiotNumber = table.Column<string>(type: "VARCHAR(12)", nullable: true),
                    CiotResponsibleTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    TollSupplierCnpj = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    TollVoucherNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    TollValue = table.Column<decimal>(type: "DECIMAL(15,2)", nullable: true),
                    InsuranceResponsible = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    InsurerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    InsurerName = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    PolicyNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AdditionalInfo = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    ProtocolNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionCode = table.Column<string>(type: "VARCHAR(5)", nullable: true),
                    RejectionReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ClosingUf = table.Column<string>(type: "VARCHAR(2)", nullable: true),
                    ClosingCityIbge = table.Column<string>(type: "VARCHAR(7)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFESTS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_CONTRACTORS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(60)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_CONTRACTORS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_CONTRACTORS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_CTES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    CteAccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: false),
                    UnloadingCityIbge = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    UnloadingCityName = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    Endorsements = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_CTES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_CTES_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_CTES_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_DRIVERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    Cpf = table.Column<string>(type: "VARCHAR(11)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    AddedByEvent = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_DRIVERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_DRIVERS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_EVENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ProtocolNumber = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StatusCode = table.Column<string>(type: "VARCHAR(5)", nullable: true),
                    StatusReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    Justification = table.Column<string>(type: "VARCHAR(255)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_EVENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_EVENTS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_LOADING_CITIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    CityIbge = table.Column<string>(type: "VARCHAR(7)", nullable: false),
                    CityName = table.Column<string>(type: "VARCHAR(60)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_LOADING_CITIES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_LOADING_CITIES_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_PAYMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractorTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    ContractorName = table.Column<string>(type: "VARCHAR(60)", nullable: false),
                    Total = table.Column<decimal>(type: "DECIMAL(15,2)", nullable: false),
                    IndPag = table.Column<int>(type: "int", nullable: false),
                    Pix = table.Column<string>(type: "VARCHAR(60)", nullable: true),
                    BankCode = table.Column<string>(type: "VARCHAR(5)", nullable: true),
                    Agency = table.Column<string>(type: "VARCHAR(10)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_PAYMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_PAYMENTS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_ROUTE_UFS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    Uf = table.Column<string>(type: "VARCHAR(2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_ROUTE_UFS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_ROUTE_UFS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_SEALS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "VARCHAR(60)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_SEALS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_SEALS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_VEHICLES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNum = table.Column<int>(type: "int", nullable: false),
                    IsTraction = table.Column<bool>(type: "bit", nullable: false),
                    Plate = table.Column<string>(type: "VARCHAR(8)", nullable: false),
                    Renavam = table.Column<string>(type: "VARCHAR(11)", nullable: true),
                    TareKg = table.Column<int>(type: "int", nullable: false),
                    CapacityKg = table.Column<int>(type: "int", nullable: false),
                    CapacityM3 = table.Column<int>(type: "int", nullable: false),
                    BodyType = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    WheelType = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    Uf = table.Column<string>(type: "VARCHAR(2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_VEHICLES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_VEHICLES_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_XMLS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Xml = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_XMLS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_XMLS_TRANSPORT_MANIFESTS_ManifestKey",
                        column: x => x.ManifestKey,
                        principalTable: "TRANSPORT_MANIFESTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_PAYMENT_COMPONENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(15,2)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(60)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_PAYMENT_COMPONENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_PAYMENT_COMPONENTS_TRANSPORT_MANIFEST_PAYMENTS_PaymentKey",
                        column: x => x.PaymentKey,
                        principalTable: "TRANSPORT_MANIFEST_PAYMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(15,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS_TRANSPORT_MANIFEST_PAYMENTS_PaymentKey",
                        column: x => x.PaymentKey,
                        principalTable: "TRANSPORT_MANIFEST_PAYMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_CONTRACTORS_ManifestKey",
                table: "TRANSPORT_MANIFEST_CONTRACTORS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_CTES_ManifestKey",
                table: "TRANSPORT_MANIFEST_CTES",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_CTES_TransportDocumentKey",
                table: "TRANSPORT_MANIFEST_CTES",
                column: "TransportDocumentKey",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_DRIVERS_ManifestKey",
                table: "TRANSPORT_MANIFEST_DRIVERS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_EVENTS_ManifestKey",
                table: "TRANSPORT_MANIFEST_EVENTS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_LOADING_CITIES_ManifestKey",
                table: "TRANSPORT_MANIFEST_LOADING_CITIES",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_PAYMENT_COMPONENTS_PaymentKey",
                table: "TRANSPORT_MANIFEST_PAYMENT_COMPONENTS",
                column: "PaymentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS_PaymentKey",
                table: "TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS",
                column: "PaymentKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_PAYMENTS_ManifestKey",
                table: "TRANSPORT_MANIFEST_PAYMENTS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_ROUTE_UFS_ManifestKey",
                table: "TRANSPORT_MANIFEST_ROUTE_UFS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_SEALS_ManifestKey",
                table: "TRANSPORT_MANIFEST_SEALS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_VEHICLES_ManifestKey",
                table: "TRANSPORT_MANIFEST_VEHICLES",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFEST_XMLS_ManifestKey",
                table: "TRANSPORT_MANIFEST_XMLS",
                column: "ManifestKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFESTS_AccessKey",
                table: "TRANSPORT_MANIFESTS",
                column: "AccessKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_MANIFESTS_BranchCode_Series_Number",
                table: "TRANSPORT_MANIFESTS",
                columns: new[] { "BranchCode", "Series", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_CONTRACTORS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_CTES");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_DRIVERS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_EVENTS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_LOADING_CITIES");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_PAYMENT_COMPONENTS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_PAYMENT_INSTALLMENTS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_ROUTE_UFS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_SEALS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_VEHICLES");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_XMLS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFEST_PAYMENTS");

            migrationBuilder.DropTable(
                name: "TRANSPORT_MANIFESTS");
        }
    }
}
