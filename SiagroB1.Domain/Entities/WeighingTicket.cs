using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("WEIGHTING_TICKETS")]
public class WeighingTicket : BaseEntity<string>
{
    public DateTime Date { get; set; }

    public int Time { get; set; }
    
    public WeighingTicketOperation Operation { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string TruckKey { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [MaxLength(10)]
    public required string TruckDriverKey { get; set; }

    public int FirstWeighValue { get; set; } = 0;

    public DateTimeOffset FirstWeighDateTime { get; set; }

    public int SecondWeighValue { get; set; } = 0;

    public DateTimeOffset SecondWeighDateTime { get; set; }

    public int GrossWeight { get; set; } = 0;
    
    public ICollection<QualityInspection> QualityInspections { get; set; } = [];
}