using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS")]
public class PurchaseContract : BaseEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Complement { get; set; }

    public DateTime CreationDate { get; set; } = DateTime.Now;

    public ContractType Type { get; set; }

    public ContractStatus Status { get; set; }

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string BusinessParterKey { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightCost { get; set; } = 0;

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]  
    public required string ProductKey { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    [ForeignKey("UnitOfMeasure")]
    public required string UnitOfMeasureKey { get; set; }
    public virtual UnitOfMeasure? UnitOfMeasure { get; set; }
    
    [ForeignKey("HarvestSeason")]
    public required Guid HarvestSeasonKey { get; set; }
    public virtual HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalVolume { get; set; } = 0;
    
    [ForeignKey("DeliveryLocation")]
    public required Guid DeliveryLocationId { get; set; }
    public virtual Warehouse? DeliveryLocation { get; set; }
    
    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? Comments { get; set; }
    
    public ICollection<PurchaseContractPriceFixation> PriceFixations { get; set; } = [];
    
    public decimal FixedVolume => PriceFixations?.Sum(x => x.FixationVolume) ?? 0;
    
    public decimal AvailableVolume => TotalVolume - FixedVolume;
}