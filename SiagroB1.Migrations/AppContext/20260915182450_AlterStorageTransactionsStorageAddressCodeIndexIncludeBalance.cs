using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterStorageTransactionsStorageAddressCodeIndexIncludeBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressCode")
                .Annotation("SqlServer:Include", new[] { "TransactionType", "TransactionStatus", "NetWeight" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressCode");
        }
    }
}
