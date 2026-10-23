using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Um seguro averbado na AT&amp;M para o CT-e (uma linha por seguro devolvido).</summary>
[Table("TRANSPORT_DOCUMENT_ENDORSEMENTS")]
public class TransportDocumentEndorsement
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    [Column(TypeName = "VARCHAR(40)")] public string? EndorsementNumber { get; set; }
    [Column(TypeName = "VARCHAR(14)")] public string? InsurerTaxId { get; set; }
    [Column(TypeName = "VARCHAR(115)")] public string? InsurerName { get; set; }
    [Column(TypeName = "VARCHAR(40)")] public string? PolicyNumber { get; set; }
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")] public decimal Amount { get; set; }
    [Column(TypeName = "VARCHAR(40)")] public string? Branch { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? MovementType { get; set; }
}
