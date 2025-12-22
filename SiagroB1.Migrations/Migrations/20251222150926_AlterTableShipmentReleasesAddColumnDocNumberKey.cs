using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableShipmentReleasesAddColumnDocNumberKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(14)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocNumberKey",
                table: "SHIPMENT_RELEASES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_DocNumberKey",
                table: "SHIPMENT_RELEASES",
                column: "DocNumberKey");

            migrationBuilder.AddForeignKey(
                name: "FK_SHIPMENT_RELEASES_DOC_NUMBERS_DocNumberKey",
                table: "SHIPMENT_RELEASES",
                column: "DocNumberKey",
                principalTable: "DOC_NUMBERS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SHIPMENT_RELEASES_DOC_NUMBERS_DocNumberKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropIndex(
                name: "IX_SHIPMENT_RELEASES_DocNumberKey",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "BranchCode",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "DocNumberKey",
                table: "SHIPMENT_RELEASES");
        }
    }
}
