using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Reboque do conjunto, na posição 1 a 3. O mesmo reboque pode estar em mais de um conjunto.</summary>
[Table("TRUCK_COMBINATION_TRAILERS")]
public class TruckCombinationTrailer
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public required string CombinationCode { get; set; }

    public int Position { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    [ForeignKey(nameof(Trailer))]
    public required string TrailerTruckCode { get; set; }
    public virtual Truck? Trailer { get; set; }
}
