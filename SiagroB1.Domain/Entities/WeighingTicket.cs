using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("WEIGHTING_TICKETS")]
public class WeighingTicket : BaseEntity<string>
{
    public DateTime Date { get; set; }

    public int Time { get; set; }
    
    [MaxLength(50)]
    [Column(TypeName = "VARCHAR(50)")]
    public required string Operation { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string CardCode { get; set; }
    
    [MaxLength(10)]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("Truck")]
    public required string TruckKey { get; set; }
    public Truck? Truck { get; set; }
    
    [MaxLength(10)]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("TruckDriver")]
    public required string TruckDriverKey { get; set; }
    public TruckDriver? TruckDriver { get; set; }

    public int FirstWeighValue { get; set; } = 0;

    public DateTimeOffset FirstWeighDateTime { get; set; }

    public int SecondWeighValue { get; set; } = 0;

    public DateTimeOffset SecondWeighDateTime { get; set; }

    public int GrossWeight { get; set; } = 0;
    
    public ICollection<QualityInspection> QualityInspections { get; set; } = [];
}