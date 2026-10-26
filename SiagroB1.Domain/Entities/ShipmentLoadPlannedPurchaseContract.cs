using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Contrato de compra PLANEJADO para a carga (GAC-1185): vínculo opcional e informativo, sem
/// efeito em saldo, peso ou valor. O contrato efetivo continua sendo o das notas da carga.
/// Um contrato aparece no máximo uma vez por carga.
/// </summary>
[Table("SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS")]
[Index(nameof(ShipmentLoadKey), nameof(ContractKey), IsUnique = true)]
public class ShipmentLoadPlannedPurchaseContract
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public Guid ContractKey { get; set; }
    public virtual PurchaseContract? Contract { get; set; }

    /// <summary>Quantidade planejada para a carga neste contrato; opcional.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal? PlannedQuantity { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string CreatedBy { get; set; } = string.Empty;
}
