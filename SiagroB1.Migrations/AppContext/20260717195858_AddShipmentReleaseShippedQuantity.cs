using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentReleaseShippedQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "SHIPMENT_RELEASES",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippedQuantity",
                table: "SHIPMENT_RELEASES",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            // Backfill: usado = Σ(SalesShipment.Net) − Σ(SalesShipmentReturn.Net), status <> Cancelled.
            migrationBuilder.Sql(@"
                UPDATE SR
                SET SR.ShippedQuantity = ISNULL((
                    SELECT SUM(CASE
                                 WHEN t.TransactionType = 7  THEN t.NetWeight
                                 WHEN t.TransactionType = 12 THEN -t.NetWeight
                                 ELSE 0 END)
                    FROM STORAGE_TRANSACTIONS t
                    WHERE t.ShipmentReleaseKey = SR.[Key]
                      AND t.TransactionStatus <> 2
                      AND t.TransactionType IN (7, 12)
                ), 0)
                FROM SHIPMENT_RELEASES SR;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "SHIPMENT_RELEASES");

            migrationBuilder.DropColumn(
                name: "ShippedQuantity",
                table: "SHIPMENT_RELEASES");
        }
    }
}
