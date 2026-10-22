using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Quantidade da carga (infQ).</summary>
[Table("TRANSPORT_DOCUMENT_QUANTITIES")]
public class TransportDocumentQuantity
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    /// <summary>00 M3, 01 KG, 02 TON, 03 UNIDADE, 04 LITROS, 05 MMBTU.</summary>
    [Column(TypeName = "VARCHAR(2)")] public required string UnitCode { get; set; }
    /// <summary>Tipo da medida, ex. VOLUME.</summary>
    [Column(TypeName = "VARCHAR(20)")] public required string MeasureType { get; set; }
    [Column(TypeName = "DECIMAL(15,4) DEFAULT 0")] public decimal Quantity { get; set; }
}
