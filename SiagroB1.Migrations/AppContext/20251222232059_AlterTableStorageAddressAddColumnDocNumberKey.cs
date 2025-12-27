#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageAddressAddColumnDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_ADDRESSES_DOC_TYPES_DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "STORAGE_ADDRESSES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_DocNumberKey",
                table: "STORAGE_ADDRESSES",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_ADDRESSES_DOC_NUMBERS_DocNumberKey",
                table: "STORAGE_ADDRESSES",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_ADDRESSES_DOC_NUMBERS_DocNumberKey",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_DocNumberKey",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_DocTypeCode",
                table: "STORAGE_ADDRESSES",
                column: "DocTypeCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_ADDRESSES_DOC_TYPES_DocTypeCode",
                table: "STORAGE_ADDRESSES",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }
    }
}
