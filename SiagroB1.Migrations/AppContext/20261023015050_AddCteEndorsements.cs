using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddCteEndorsements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndorsedAt",
                table: "TRANSPORT_DOCUMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndorsementCancellationProtocol",
                table: "TRANSPORT_DOCUMENTS",
                type: "VARCHAR(40)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndorsementError",
                table: "TRANSPORT_DOCUMENTS",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndorsementProtocol",
                table: "TRANSPORT_DOCUMENTS",
                type: "VARCHAR(40)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndorsementStatus",
                table: "TRANSPORT_DOCUMENTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "TRANSPORT_DOCUMENT_ENDORSEMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransportDocumentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndorsementNumber = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    InsurerTaxId = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    InsurerName = table.Column<string>(type: "VARCHAR(115)", nullable: true),
                    PolicyNumber = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    Amount = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    Branch = table.Column<string>(type: "VARCHAR(40)", nullable: true),
                    MovementType = table.Column<string>(type: "VARCHAR(20)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRANSPORT_DOCUMENT_ENDORSEMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRANSPORT_DOCUMENT_ENDORSEMENTS_TRANSPORT_DOCUMENTS_TransportDocumentKey",
                        column: x => x.TransportDocumentKey,
                        principalTable: "TRANSPORT_DOCUMENTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TRANSPORT_DOCUMENT_ENDORSEMENTS_TransportDocumentKey",
                table: "TRANSPORT_DOCUMENT_ENDORSEMENTS",
                column: "TransportDocumentKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TRANSPORT_DOCUMENT_ENDORSEMENTS");

            migrationBuilder.DropColumn(
                name: "EndorsedAt",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "EndorsementCancellationProtocol",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "EndorsementError",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "EndorsementProtocol",
                table: "TRANSPORT_DOCUMENTS");

            migrationBuilder.DropColumn(
                name: "EndorsementStatus",
                table: "TRANSPORT_DOCUMENTS");
        }
    }
}
