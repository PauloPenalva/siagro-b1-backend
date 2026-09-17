using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Linha de distribuição da perda da Conferência de Saldo de Armazém (GAC-1164 §9): quanto da perda
/// cai em cada liberação. A quebra é custo da empresa, mas precisa CONSUMIR a liberação — senão o
/// saldo a embarcar dos contratos fica maior que o grão que existe no armazém e sobra um resíduo
/// que nunca é embarcado.
/// </summary>
[Table("WAREHOUSE_RECONCILIATION_RELEASES")]
public class WarehouseReconciliationRelease
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid WarehouseReconciliationKey { get; set; }
    public virtual WarehouseReconciliation? WarehouseReconciliation { get; set; }

    public Guid ShipmentReleaseKey { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal Quantity { get; set; }

    /// <summary>Compra(8) gerada na aprovação; nula quando a liberação não tem perna de compra.</summary>
    public Guid? PurchaseStorageTransactionKey { get; set; }

    /// <summary>Perda(13) gerada na aprovação.</summary>
    public Guid? LossStorageTransactionKey { get; set; }
}
