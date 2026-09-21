using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// Minutas de contrato, modelos e signatários (spec 2026-09-21, fase 1).
    /// <inheritdoc />
    public partial class AddContractDraftsAndTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BUSINESS_PARTNER_SIGNATORIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_BUSINESS_PARTNER_SIGNATORIES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "COMPANY_SIGNATORIES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_COMPANY_SIGNATORIES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_COMPANY_SIGNATORIES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_TEMPLATES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Title = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    ContractType = table.Column<int>(type: "int", nullable: false),
                    BodyHtml = table.Column<string>(type: "VARCHAR(MAX)", nullable: false),
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
                    table.PrimaryKey("PK_CONTRACT_TEMPLATES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_DRAFTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContractCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    TemplateKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    BodyHtml = table.Column<string>(type: "VARCHAR(MAX)", nullable: false),
                    PlaceholdersJson = table.Column<string>(type: "VARCHAR(MAX)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "VARCHAR(20)", nullable: true),
                    ExternalDocumentId = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "VARCHAR(1000)", nullable: true),
                    SignedAttachmentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_DRAFTS", x => x.Key);
                    table.CheckConstraint("CK_CONTRACT_DRAFTS_ONE_CONTRACT", "([PurchaseContractKey] IS NULL AND [SalesContractKey] IS NOT NULL) OR ([PurchaseContractKey] IS NOT NULL AND [SalesContractKey] IS NULL)");
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFTS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFTS_CONTRACT_TEMPLATES_TemplateKey",
                        column: x => x.TemplateKey,
                        principalTable: "CONTRACT_TEMPLATES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFTS_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFTS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "CONTRACT_DRAFT_SIGNERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Side = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TaxId = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Email = table.Column<string>(type: "VARCHAR(200)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastMessage = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CONTRACT_DRAFT_SIGNERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_CONTRACT_DRAFT_SIGNERS_CONTRACT_DRAFTS_DraftKey",
                        column: x => x.DraftKey,
                        principalTable: "CONTRACT_DRAFTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BUSINESS_PARTNER_SIGNATORIES_CardCode",
                table: "BUSINESS_PARTNER_SIGNATORIES",
                column: "CardCode");

            migrationBuilder.CreateIndex(
                name: "IX_BUSINESS_PARTNER_SIGNATORIES_CardCode_Email",
                table: "BUSINESS_PARTNER_SIGNATORIES",
                columns: new[] { "CardCode", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_COMPANY_SIGNATORIES_BranchCode",
                table: "COMPANY_SIGNATORIES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_COMPANY_SIGNATORIES_Email",
                table: "COMPANY_SIGNATORIES",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFT_SIGNERS_DraftKey",
                table: "CONTRACT_DRAFT_SIGNERS",
                column: "DraftKey");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_BranchCode",
                table: "CONTRACT_DRAFTS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_DocNumberKey",
                table: "CONTRACT_DRAFTS",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_ExternalDocumentId",
                table: "CONTRACT_DRAFTS",
                column: "ExternalDocumentId",
                unique: true,
                filter: "[ExternalDocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_PurchaseContractKey",
                table: "CONTRACT_DRAFTS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_PurchaseContractKey_SalesContractKey_Sequence",
                table: "CONTRACT_DRAFTS",
                columns: new[] { "PurchaseContractKey", "SalesContractKey", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_SalesContractKey",
                table: "CONTRACT_DRAFTS",
                column: "SalesContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_DRAFTS_TemplateKey",
                table: "CONTRACT_DRAFTS",
                column: "TemplateKey");

            migrationBuilder.CreateIndex(
                name: "IX_CONTRACT_TEMPLATES_Name",
                table: "CONTRACT_TEMPLATES",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BUSINESS_PARTNER_SIGNATORIES");

            migrationBuilder.DropTable(
                name: "COMPANY_SIGNATORIES");

            migrationBuilder.DropTable(
                name: "CONTRACT_DRAFT_SIGNERS");

            migrationBuilder.DropTable(
                name: "CONTRACT_DRAFTS");

            migrationBuilder.DropTable(
                name: "CONTRACT_TEMPLATES");
        }
    }
}
