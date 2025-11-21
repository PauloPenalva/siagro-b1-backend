using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_CONTRACTS")]
[Index("Code", "Sequence", IsUnique = true)]
public class SalesContract : BaseEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(3) NOT NULL")]
    public required string Sequence { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? Complement { get; set; }

    public DateTime? CreationDate { get; set; } = DateTime.Now;

    public ContractType Type { get; set; }
    
    public ContractStatus? Status { get; set; } = ContractStatus.Draft;

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }

    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightCost { get; set; }

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]  
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    [ForeignKey("UnitOfMeasure")]
    public required string UnitOfMeasureCode { get; set; }
    public virtual UnitOfMeasure? UnitOfMeasure { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("HarvestSeason")]
    public required string HarvestSeasonCode { get; set; }
    public virtual HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalVolume { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    public DateTime? CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; } = string.Empty;
    
    public DateTime? ApprovedAt { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? ApprovedBy { get; set; } = string.Empty;
    
    public DateTime? CanceledAt { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? CanceledBy { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(LogisticRegionCode))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal Price { get; set; } = 0;
    
    public decimal TotalPrice => 
        decimal.Round((Volume * Price), 2 , MidpointRounding.ToEven);
}