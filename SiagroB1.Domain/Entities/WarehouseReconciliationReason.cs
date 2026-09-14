using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Motivo da diferença apurada numa Conferência de Saldo de Armazém (quebra técnica,
/// sinistro, umidade...). Cadastro editável; motivo em uso só pode ser desativado.
/// </summary>
[Table("WAREHOUSE_RECONCILIATION_REASONS")]
public class WarehouseReconciliationReason : BaseEntity
{
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    public bool Active { get; set; } = true;
}
