#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableStorageAddressesAddCodePK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressKey",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_STORAGE_ADDRESSES",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPPING_ORDERS_StorageAddressKey",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "StorageAddressKey",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "StorageAddressKey",
                table: "SHIPPING_ORDERS");

            migrationBuilder.AddColumn<string>(
                name: "StorageAddressCode",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAddressCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(50)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Comments",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(500)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageAddressCode",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_STORAGE_ADDRESSES",
                table: "STORAGE_ADDRESSES",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressCode",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ORDERS_StorageAddressCode",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressCode",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressCode",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressCode",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropPrimaryKey(
                name: "PK_STORAGE_ADDRESSES",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPPING_ORDERS_StorageAddressCode",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "StorageAddressCode",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "StorageAddressCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "StorageAddressCode",
                table: "SHIPPING_ORDERS");

            migrationBuilder.AddColumn<Guid>(
                name: "StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AddColumn<Guid>(
                name: "Key",
                table: "STORAGE_ADDRESSES",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<string>(
                name: "Comments",
                table: "SHIPPING_ORDERS",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(500)",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StorageAddressKey",
                table: "SHIPPING_ORDERS",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_STORAGE_ADDRESSES",
                table: "STORAGE_ADDRESSES",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHING_TICKETS_StorageAddressKey",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_ADDRESSES_Code",
                table: "STORAGE_ADDRESSES",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_ORDERS_StorageAddressKey",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPPING_ORDERS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "SHIPPING_ORDERS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "STORAGE_TRANSACTIONS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");

            migrationBuilder.AddForeignKey(
                name: "FK_WEIGHING_TICKETS_STORAGE_ADDRESSES_StorageAddressKey",
                table: "WEIGHING_TICKETS",
                column: "StorageAddressKey",
                principalTable: "STORAGE_ADDRESSES",
                principalColumn: "Key");
        }
    }
}
