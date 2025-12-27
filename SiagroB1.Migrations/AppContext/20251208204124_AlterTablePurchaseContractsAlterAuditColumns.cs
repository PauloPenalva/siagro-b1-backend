#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAlterAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "WEIGHING_TICKETS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "WEIGHING_TICKETS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "WEIGHING_TICKETS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "WEIGHING_TICKETS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "WEIGHING_TICKETS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "STORAGE_TRANSACTIONS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "STORAGE_ADDRESSES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "SHIPPING_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "SHIPPING_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "SHIPPING_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SHIPPING_ORDERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "SHIPPING_ORDERS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "SHIPMENT_RELEASES",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(100)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "SHIPMENT_RELEASES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SHIPMENT_RELEASES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                table: "SALES_INVOICES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanceledBy",
                table: "SALES_INVOICES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "SALES_INVOICES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SALES_INVOICES",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "SALES_INVOICES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_ALLOCATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Volume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_ALLOCATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_ALLOCATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_ALLOCATIONS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_ALLOCATIONS",
                column: "PurchaseContractKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_ALLOCATIONS");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "WEIGHING_TICKETS");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "SHIPPING_ORDERS");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CanceledBy",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "SALES_INVOICES");

            migrationBuilder.AlterColumn<string>(
                name: "CreatedBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(100)",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "SHIPMENT_RELEASES",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ApprovedBy",
                table: "SHIPMENT_RELEASES",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(100)",
                oldNullable: true);
        }
    }
}
