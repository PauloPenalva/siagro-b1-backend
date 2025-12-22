using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("WEIGHING_TICKETS")]
[Index("Code", IsUnique = true)]
public class WeighingTicket : DocumentEntity
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public string? Code { get; set; }
    
    public DateTime? Date { get; set; } = DateTime.Now.Date;
    
    public required WeighingTicketType Type { get; set; }

    public WeighingTicketStatus? Status { get; set; } = WeighingTicketStatus.Waiting;

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    [MaxLength(50)]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    [MaxLength(15)]
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

    public DateTimeOffset? FirstWeighDateTime { get; set; }

    public int SecondWeighValue { get; set; } = 0;

    public DateTimeOffset? SecondWeighDateTime { get; set; }

    [NotMapped]
    public int GrossWeight => int.Abs(FirstWeighValue - SecondWeighValue);
    
    public ICollection<QualityInspection> QualityInspections { get; set; } = [];

    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? Comments { get; set; }
    
    public Guid? StorageAddressKey { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingCostCode { get; set; }

    public WeighingTicketStage? Stage { get; set; } = WeighingTicketStage.ReadyForFirstWeighing;
}