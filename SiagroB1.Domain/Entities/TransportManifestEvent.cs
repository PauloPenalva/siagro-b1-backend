using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Evento do MDF-e (encerramento, cancelamento, inclusão de condutor).</summary>
[Table("TRANSPORT_MANIFEST_EVENTS")]
public class TransportManifestEvent
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public MdfeEventType Type { get; set; }
    public int Sequence { get; set; } = 1;
    [Column(TypeName = "VARCHAR(20)")] public string? ProtocolNumber { get; set; }
    public DateTime? RegisteredAt { get; set; }
    [Column(TypeName = "VARCHAR(5)")] public string? StatusCode { get; set; }
    [Column(TypeName = "VARCHAR(500)")] public string? StatusReason { get; set; }
    [Column(TypeName = "VARCHAR(255)")] public string? Justification { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    [Column(TypeName = "VARCHAR(100)")] public string? CreatedBy { get; set; }
}
