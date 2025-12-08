using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageAddressesAddColumnDocTypeCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "STORAGE_TRANSACTIONS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocTypeCode",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DocTypeCode");

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

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_TYPES_DocTypeCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DocTypeCode",
                principalTable: "DOC_TYPES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_ADDRESSES_DOC_TYPES_DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_DOC_TYPES_DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "DocTypeCode",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "STORAGE_ADDRESSES");
        }
    }
}
