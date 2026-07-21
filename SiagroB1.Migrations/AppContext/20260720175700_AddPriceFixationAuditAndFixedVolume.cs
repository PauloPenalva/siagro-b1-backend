using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPriceFixationAuditAndFixedVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalComments",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(500)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RowId",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FixedVolume",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            // Backfill: sem isso todo contrato existente ficaria com FixedVolume = 0
            // e apareceria como totalmente disponível para fixar.
            // Status IN (0, 1) => InApproval, Confirmed (os que reservam volume).
            migrationBuilder.Sql(@"
                UPDATE pc
                   SET pc.FixedVolume = ISNULL(f.Total, 0)
                  FROM PURCHASE_CONTRACTS pc
                  LEFT JOIN (
                        SELECT PurchaseContractKey, SUM(FixationVolume) AS Total
                          FROM PURCHASE_CONTRACTS_PRICE_FIXATIONS
                         WHERE Status IN (0, 1)
                         GROUP BY PurchaseContractKey
                  ) f ON f.PurchaseContractKey = pc.[Key];
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovalComments",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "RowId",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropColumn(
                name: "FixedVolume",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
