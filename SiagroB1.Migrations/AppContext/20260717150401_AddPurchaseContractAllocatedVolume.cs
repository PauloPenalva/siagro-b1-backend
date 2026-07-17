using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseContractAllocatedVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AllocatedVolume",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "PURCHASE_CONTRACTS",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            // Backfill: soma (com sinal) das alocações existentes por contrato.
            migrationBuilder.Sql(@"
                UPDATE PC
                SET PC.AllocatedVolume = ISNULL((
                    SELECT SUM(a.Volume)
                    FROM PURCHASE_CONTRACTS_ALLOCATIONS a
                    WHERE a.PurchaseContractKey = PC.[Key]
                ), 0)
                FROM PURCHASE_CONTRACTS PC;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllocatedVolume",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
