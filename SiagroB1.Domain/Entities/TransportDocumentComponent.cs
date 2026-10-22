using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Componente do valor da prestação (Comp).</summary>
[Table("TRANSPORT_DOCUMENT_COMPONENTS")]
public class TransportDocumentComponent
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    [Column(TypeName = "VARCHAR(15)")] public required string Name { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal Value { get; set; }
}
