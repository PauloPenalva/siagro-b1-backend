using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class BackfillShippedQuantityFromPurchase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Realinha ShippedQuantity à regra nova: Σ(Purchase.Net) − Σ(PurchaseReturn.Net).
            // Antes contava os romaneios de venda (7/12), que são a CÓPIA criada por
            // ShippingTransactionsCreateService e cujo confirm faz NetWeight = GrossWeight —
            // ou seja, mediam bruto. O par de compra carrega o líquido após descontos de
            // qualidade, que é o mesmo valor usado na alocação do contrato.
            // Tipos: Purchase = 8, PurchaseReturn = 9. Status Cancelled = 2.
            migrationBuilder.Sql(@"
                UPDATE SR
                SET SR.ShippedQuantity = ISNULL((
                    SELECT SUM(CASE
                                 WHEN t.TransactionType = 8 THEN t.NetWeight
                                 WHEN t.TransactionType = 9 THEN -t.NetWeight
                                 ELSE 0 END)
                    FROM STORAGE_TRANSACTIONS t
                    WHERE t.ShipmentReleaseKey = SR.[Key]
                      AND t.TransactionStatus <> 2
                      AND t.TransactionType IN (8, 9)
                ), 0)
                FROM SHIPMENT_RELEASES SR;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaura a regra antiga (SalesShipment = 7, SalesShipmentReturn = 12).
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
    }
}
