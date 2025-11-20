using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableStorageAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "STORAGE_ADDRESSES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OwnershipType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_ADDRESSES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "STORAGE_TRANSACTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageAddressId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TransactionTime = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    NetWeight = table.Column<decimal>(type: "decimal(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_TRANSACTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressId",
                        column: x => x.StorageAddressId,
                        principalTable: "STORAGE_ADDRESSES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressId",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "STORAGE_ADDRESSES");
        }
    }
}
