using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>UF de percurso (infPercurso), na ordem de LineNum.</summary>
[Table("TRANSPORT_MANIFEST_ROUTE_UFS")]
public class TransportManifestRouteUf
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public int LineNum { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string Uf { get; set; }
}
