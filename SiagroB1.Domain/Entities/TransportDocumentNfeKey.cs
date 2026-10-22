using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Chave de NF-e transportada.</summary>
[Table("TRANSPORT_DOCUMENT_NFE_KEYS")]
public class TransportDocumentNfeKey
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    [Column(TypeName = "VARCHAR(44)")] public required string AccessKey { get; set; }
    public int Order { get; set; }
}
