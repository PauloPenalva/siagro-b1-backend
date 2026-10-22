using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Conjunto de veículos (só STANDALONE): uma tração e até 3 reboques, na ordem. O CT-e e o MDF-e
/// escolhem o conjunto e copiam as placas, para que mudar o conjunto não altere documento emitido.
/// </summary>
[Table("TRUCK_COMBINATIONS")]
public class TruckCombination
{
    [Key]
    [Column(TypeName = "VARCHAR(20)")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    [ForeignKey(nameof(Traction))]
    public required string TractionTruckCode { get; set; }
    public virtual Truck? Traction { get; set; }

    [Column(TypeName = "VARCHAR(11)")]
    [ForeignKey(nameof(DefaultDriver))]
    public string? DefaultDriverCode { get; set; }
    public virtual TruckDriver? DefaultDriver { get; set; }

    public bool Inactive { get; set; }

    public virtual ICollection<TruckCombinationTrailer> Trailers { get; set; } = new List<TruckCombinationTrailer>();
}
