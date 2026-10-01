using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Parcela do ticket de descarga numa linha de documento de saída (GAC-1171, rateio).
/// </summary>
/// <remarks>
/// O ticket pesa o caminhão inteiro, e a carga pode ter vários documentos de saída: o peso do papel
/// (<see cref="ShipmentLoadDischarge.DischargedQuantity"/>) é rateado entre as linhas, e a soma das
/// parcelas fecha com ele (<c>ShipmentLoadDischargeRules.NormalizeDistribution</c>).
/// <para>
/// Guarda a nota E a linha: a linha é o alvo do peso, a nota existe para a aba chegar ao número dela
/// com um <c>$expand</c> de um nível. O par é resolvido no servidor a partir da linha.
/// </para>
/// </remarks>
[Table("SHIPMENT_LOAD_DISCHARGE_ITEMS")]
[Index(nameof(DischargeKey), nameof(SalesInvoiceItemKey), IsUnique = true)]
[Index(nameof(SalesInvoiceItemKey))]
public class ShipmentLoadDischargeItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid DischargeKey { get; set; }
    public virtual ShipmentLoadDischarge? Discharge { get; set; }

    public Guid SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid SalesInvoiceItemKey { get; set; }
    public virtual SalesInvoiceItem? SalesInvoiceItem { get; set; }

    /// <summary>Parcela do peso do ticket. Mesma escala de <c>DischargedQuantity</c>.</summary>
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }
}
