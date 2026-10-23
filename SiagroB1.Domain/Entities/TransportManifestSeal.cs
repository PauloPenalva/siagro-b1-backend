using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Lacre (lacres/nLacre).</summary>
[Table("TRANSPORT_MANIFEST_SEALS")]
public class TransportManifestSeal
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    [Column(TypeName = "VARCHAR(60)")] public required string Number { get; set; }
}
