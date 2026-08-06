using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class CreatePurchaseInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ORDEM IMPORTA: o scaffold gerou os DropTable de CUSTOMER_RETURNS aqui no topo, o que
            // apagaria os dados antes de haver para onde copiá-los. Foram movidos para o FIM do
            // Up(), depois da criação das tabelas novas e da migração das linhas.
            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceType = table.Column<int>(type: "int", nullable: false),
                    IssuerType = table.Column<int>(type: "int", nullable: false),
                    InvoiceStatus = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    TaxDocumentNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    TaxDocumentSeries = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    ChaveNFe = table.Column<string>(type: "VARCHAR(44)", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalDocumentValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    TaxPayerComments = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    GrossWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    NetWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    TruckCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    TruckingCompanyCode = table.Column<string>(type: "VARCHAR(15)", nullable: true),
                    TruckingCompanyName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    FreightTerms = table.Column<int>(type: "int", nullable: false),
                    PurchaseInvoiceOriginKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    XmlFileName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    XmlData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: true),
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
                    table.PrimaryKey("PK_PURCHASE_INVOICES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_PURCHASE_INVOICES_PurchaseInvoiceOriginKey",
                        column: x => x.PurchaseInvoiceOriginKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICES_CHANGE_LOGS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    Field = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    OldValue = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    NewValue = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICES_CHANGE_LOGS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_CHANGE_LOGS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICES_COMMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CommentedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CommentedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CommentText = table.Column<string>(type: "VARCHAR(500)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICES_COMMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_COMMENTS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_INVOICES_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PurchaseInvoiceItemOriginKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_INVOICES_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_INVOICES_ITEMS_PurchaseInvoiceItemOriginKey",
                        column: x => x.PurchaseInvoiceItemOriginKey,
                        principalTable: "PURCHASE_INVOICES_ITEMS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_ITEMS_PURCHASE_INVOICES_PurchaseInvoiceKey",
                        column: x => x.PurchaseInvoiceKey,
                        principalTable: "PURCHASE_INVOICES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_INVOICES_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_BranchCode",
                table: "PURCHASE_INVOICES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ChaveNFe",
                table: "PURCHASE_INVOICES",
                column: "ChaveNFe",
                unique: true,
                filter: "[ChaveNFe] IS NOT NULL AND [InvoiceStatus] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_DocNumberKey",
                table: "PURCHASE_INVOICES",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_PurchaseInvoiceOriginKey",
                table: "PURCHASE_INVOICES",
                column: "PurchaseInvoiceOriginKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_CHANGE_LOGS_PurchaseInvoiceKey",
                table: "PURCHASE_INVOICES_CHANGE_LOGS",
                column: "PurchaseInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_COMMENTS_PurchaseInvoiceKey",
                table: "PURCHASE_INVOICES_COMMENTS",
                column: "PurchaseInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_PurchaseInvoiceItemOriginKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "PurchaseInvoiceItemOriginKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_PurchaseInvoiceKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "PurchaseInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_INVOICES_ITEMS_SalesInvoiceItemKey",
                table: "PURCHASE_INVOICES_ITEMS",
                column: "SalesInvoiceItemKey");

            // ---------------------------------------------------------------------------------
            // Migração dos dados: a devolução de cliente vira documento de entrada tipo Return.
            //
            // Guardado por OBJECT_ID porque CUSTOMER_RETURNS nunca foi commitada e pode não existir
            // no ambiente — esta migration precisa rodar tanto em base que a tem quanto em base que
            // nunca a viu.
            //
            // O que NÃO é inventado aqui:
            //   * ApprovedAt/ApprovedBy ficam como estavam (tipicamente vazios). As linhas viram
            //     Confirmed porque nasceram sob um modelo SEM etapa de aprovação — carimbar um
            //     aprovador que nunca existiu seria fabricar auditoria.
            //   * BranchCode só é preenchido quando a base tem UMA única filial. Com várias, fica
            //     NULL: uma filial em branco é visível e pede correção; uma filial errada num
            //     documento fiscal passa despercebida.
            // ---------------------------------------------------------------------------------
            migrationBuilder.Sql(@"
IF OBJECT_ID('CUSTOMER_RETURNS', 'U') IS NOT NULL
BEGIN
    DECLARE @BranchCode VARCHAR(14) =
        (SELECT CASE WHEN COUNT(*) = 1 THEN MIN(Code) ELSE NULL END FROM BRANCHS);

    INSERT INTO PURCHASE_INVOICES
        ([Key], BranchCode, InvoiceType, IssuerType, InvoiceStatus,
         CardCode, CardName, TaxDocumentNumber, TaxDocumentSeries, ChaveNFe,
         IssueDate, PostingDate, TotalDocumentValue, TaxPayerComments,
         GrossWeight, NetWeight, FreightTerms,
         XmlFileName, XmlData,
         CreatedAt, CreatedBy, UpdatedAt, UpdatedBy,
         ApprovedAt, ApprovedBy, CanceledAt, CanceledBy)
    SELECT
        cr.[Key], @BranchCode,
        1,                              -- PurchaseInvoiceType.Return
        0,                              -- DocumentIssuerType.ThirdParty
        CASE cr.[Status]
            WHEN 1 THEN 2               -- CustomerReturnStatus.Cancelled -> InvoiceStatus.Cancelled
            ELSE 1                      -- Registered                     -> InvoiceStatus.Confirmed
        END,
        cr.CardCode, cr.CardName, cr.DocumentNumber, cr.DocumentSeries, cr.AccessKey,
        cr.IssueDate, cr.IssueDate, cr.TotalValue, cr.TaxPayerComments,
        0, 0, 0,
        cr.XmlFileName, cr.XmlData,
        cr.CreatedAt, cr.CreatedBy, cr.UpdatedAt, cr.UpdatedBy,
        cr.ApprovedAt, cr.ApprovedBy, cr.CanceledAt, cr.CanceledBy
    FROM CUSTOMER_RETURNS cr;

    -- A AMARRAÇÃO (SalesInvoiceItemKey) é o dado que não pode se perder: é dela que saem a
    -- Quebra Apurada e a Diferença na tela.
    INSERT INTO PURCHASE_INVOICES_ITEMS
        ([Key], PurchaseInvoiceKey, ItemCode, ItemName, Quantity, UnitPrice, SalesInvoiceItemKey)
    SELECT
        cri.[Key], cri.CustomerReturnKey, cri.ItemCode, cri.ItemName,
        cri.Quantity, cri.UnitPrice, cri.SalesInvoiceItemKey
    FROM CUSTOMER_RETURNS_ITEMS cri
    INNER JOIN CUSTOMER_RETURNS cr ON cr.[Key] = cri.CustomerReturnKey;
END
");

            // Só agora, com os dados já copiados.
            migrationBuilder.DropTable(
                name: "CUSTOMER_RETURNS_ITEMS");

            migrationBuilder.DropTable(
                name: "CUSTOMER_RETURNS");
        }

        /// <inheritdoc />
        /// <summary>
        /// Reverte o ESQUEMA, não os DADOS. As tabelas CUSTOMER_RETURNS voltam vazias: as linhas
        /// migradas ficam nas PURCHASE_INVOICES, que este Down() dropa. Reverter esta migration
        /// depois de aplicá-la em base com devoluções é operação destrutiva.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICES_CHANGE_LOGS");

            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICES_COMMENTS");

            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICES_ITEMS");

            migrationBuilder.DropTable(
                name: "PURCHASE_INVOICES");

            migrationBuilder.CreateTable(
                name: "CUSTOMER_RETURNS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccessKey = table.Column<string>(type: "VARCHAR(44)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocumentNumber = table.Column<string>(type: "VARCHAR(9)", nullable: true),
                    DocumentSeries = table.Column<string>(type: "VARCHAR(3)", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TaxPayerComments = table.Column<string>(type: "VARCHAR(MAX)", nullable: true),
                    TotalValue = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    XmlData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: true),
                    XmlFileName = table.Column<string>(type: "VARCHAR(200)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_RETURNS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMER_RETURNS_ITEMS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerReturnKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(30)", nullable: true),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMER_RETURNS_ITEMS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_CUSTOMER_RETURNS_ITEMS_CUSTOMER_RETURNS_CustomerReturnKey",
                        column: x => x.CustomerReturnKey,
                        principalTable: "CUSTOMER_RETURNS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_CUSTOMER_RETURNS_ITEMS_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_AccessKey",
                table: "CUSTOMER_RETURNS",
                column: "AccessKey",
                unique: true,
                filter: "[AccessKey] IS NOT NULL AND [Status] <> 1");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_ITEMS_CustomerReturnKey",
                table: "CUSTOMER_RETURNS_ITEMS",
                column: "CustomerReturnKey");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMER_RETURNS_ITEMS_SalesInvoiceItemKey",
                table: "CUSTOMER_RETURNS_ITEMS",
                column: "SalesInvoiceItemKey");
        }
    }
}
