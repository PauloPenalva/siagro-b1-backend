using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Carta de correção do CT-e.</summary>
[Table("TRANSPORT_DOCUMENT_CORRECTIONS")]
public class TransportDocumentCorrection
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    public int Sequence { get; set; }
    [Column(TypeName = "NVARCHAR(MAX)")] public string? ItemsJson { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public string? Protocol { get; set; }
    public DateTime SentAt { get; set; }
}
