using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>Motivos iniciais da Conferência de Saldo de Armazém. Idempotente por Code; GUIDs fixos.</summary>
    public partial class SeedWarehouseReconciliationReasons : Migration
    {
        private static readonly (string Key, string Code, string Description)[] Reasons =
        [
            ("6B1C0E27-3A94-4F1D-8E62-1A7B3C9D0E41", "QUEBRA_TECNICA", "Quebra técnica"),
            ("7C2D1F38-4BA5-4028-9F73-2B8C4DAE1F52", "SINISTRO", "Sinistro/Tombamento"),
            ("8D3E2049-5CB6-4139-A084-3C9D5EBF2063", "UMIDADE", "Umidade/Secagem"),
            ("9E4F315A-6DC7-424A-B195-4DAE6FC03174", "PESAGEM", "Divergência de pesagem"),
            ("AF50426B-7ED8-435B-C2A6-5EBF70D14285", "SOBRA", "Sobra de estoque"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (key, code, description) in Reasons)
            {
                migrationBuilder.Sql($"""
                    IF NOT EXISTS (SELECT 1 FROM WAREHOUSE_RECONCILIATION_REASONS WHERE Code = '{code}')
                    INSERT INTO WAREHOUSE_RECONCILIATION_REASONS
                        ([Key], Code, Description, Active, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, ApprovedBy, CanceledBy)
                    VALUES ('{key}', '{code}', N'{description}', 1, GETDATE(), 'system', GETDATE(), 'system', '', '');
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (key, _, _) in Reasons)
                migrationBuilder.Sql($"DELETE FROM WAREHOUSE_RECONCILIATION_REASONS WHERE [Key] = '{key}';");
        }
    }
}
