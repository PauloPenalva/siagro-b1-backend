using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnOwnershipTransferKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OWNERSHIP_TRANSFER",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TransferStatus = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    StorageAddressOriginCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    StorageAddressDestinationCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    UomCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
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
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OWNERSHIP_TRANSFER", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OWNERSHIP_TRANSFER_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_OWNERSHIP_TRANSFER_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_OWNERSHIP_TRANSFER_STORAGE_ADDRESSES_StorageAddressDestinationCode",
                        column: x => x.StorageAddressDestinationCode,
                        principalTable: "STORAGE_ADDRESSES",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_OWNERSHIP_TRANSFER_STORAGE_ADDRESSES_StorageAddressOriginCode",
                        column: x => x.StorageAddressOriginCode,
                        principalTable: "STORAGE_ADDRESSES",
                        principalColumn: "Code");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS",
                column: "OwnershipTransferKey");

            migrationBuilder.CreateIndex(
                name: "IX_OWNERSHIP_TRANSFER_BranchCode",
                table: "OWNERSHIP_TRANSFER",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_OWNERSHIP_TRANSFER_DocNumberKey",
                table: "OWNERSHIP_TRANSFER",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_OWNERSHIP_TRANSFER_StorageAddressDestinationCode",
                table: "OWNERSHIP_TRANSFER",
                column: "StorageAddressDestinationCode");

            migrationBuilder.CreateIndex(
                name: "IX_OWNERSHIP_TRANSFER_StorageAddressOriginCode",
                table: "OWNERSHIP_TRANSFER",
                column: "StorageAddressOriginCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_OWNERSHIP_TRANSFER_OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS",
                column: "OwnershipTransferKey",
                principalTable: "OWNERSHIP_TRANSFER",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_OWNERSHIP_TRANSFER_OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "OWNERSHIP_TRANSFER");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "OwnershipTransferKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
