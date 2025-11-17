using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("WEIGHTING_TICKETS")]
public class WeighingTicket : BaseEntity
{
    [Column(TypeName = "VARCHAR(15)")]
    public string? Code { get; set; }
    
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
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("Truck")]
    public required string TruckCode { get; set; }
    public virtual Truck? Truck { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    [ForeignKey("TruckDriver")]
    public required string TruckDriverCode { get; set; }
    public virtual TruckDriver? TruckDriver { get; set; }

    public int FirstWeighValue { get; set; } = 0;

    public DateTimeOffset FirstWeighDateTime { get; set; }

    public int SecondWeighValue { get; set; } = 0;

    public DateTimeOffset SecondWeighDateTime { get; set; }

    public int GrossWeight { get; set; } = 0;
    
    public ICollection<QualityInspection> QualityInspections { get; set; } = [];

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? Comments { get; set; }
    
    [Column(TypeName = "VARCHAR(10)  NOT NULL")]
    [ForeignKey("ProcessingCost")]
    public string? ProcessigCostCode { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
}