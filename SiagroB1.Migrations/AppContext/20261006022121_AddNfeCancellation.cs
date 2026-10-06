using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddNfeCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationError",
                table: "SALES_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationProtocol",
                table: "SALES_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationReason",
                table: "SALES_INVOICES",
                type: "VARCHAR(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NfeCancelledAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationError",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationProtocol",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(20)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NfeCancellationReason",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NfeCancelledAt",
                table: "PURCHASE_INVOICES",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NfeCancellationError",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancellationProtocol",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancellationReason",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancelledAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancellationError",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancellationProtocol",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancellationReason",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "NfeCancelledAt",
                table: "PURCHASE_INVOICES");
        }
    }
}
