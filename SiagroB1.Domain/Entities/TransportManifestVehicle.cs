using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Veículo (tração ou reboque) congelado no MDF-e: BodyType é o tpCar, WheelType o tpRod (só tração).</summary>
[Table("TRANSPORT_MANIFEST_VEHICLES")]
public class TransportManifestVehicle
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public int LineNum { get; set; }
    public bool IsTraction { get; set; }
    [Column(TypeName = "VARCHAR(8)")] public required string Plate { get; set; }
    [Column(TypeName = "VARCHAR(11)")] public string? Renavam { get; set; }
    public int TareKg { get; set; }
    public int CapacityKg { get; set; }
    public int CapacityM3 { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string BodyType { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string WheelType { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string Uf { get; set; }
}
