using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Contratante (infContratante): TaxId 14 = CNPJ, 11 = CPF.</summary>
[Table("TRANSPORT_MANIFEST_CONTRACTORS")]
public class TransportManifestContractor
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    [Column(TypeName = "VARCHAR(14)")] public required string TaxId { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public required string Name { get; set; }
}
