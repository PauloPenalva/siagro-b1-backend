using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageInvoiceDropIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STORAGE_INVOICES_StorageAddressCode_PeriodStart_PeriodEnd",
                table: "STORAGE_INVOICES");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_StorageAddressCode_PeriodStart_PeriodEnd",
                table: "STORAGE_INVOICES",
                columns: new[] { "StorageAddressCode", "PeriodStart", "PeriodEnd" },
                unique: true,
                filter: "[Status] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_STORAGE_INVOICES_StorageAddressCode_PeriodStart_PeriodEnd",
                table: "STORAGE_INVOICES");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_INVOICES_StorageAddressCode_PeriodStart_PeriodEnd",
                table: "STORAGE_INVOICES",
                columns: new[] { "StorageAddressCode", "PeriodStart", "PeriodEnd" },
                unique: true);
        }
    }
}
