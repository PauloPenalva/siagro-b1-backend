using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Município de carregamento (infMunCarrega).</summary>
[Table("TRANSPORT_MANIFEST_LOADING_CITIES")]
public class TransportManifestLoadingCity
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public int LineNum { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public required string CityIbge { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public required string CityName { get; set; }
}
