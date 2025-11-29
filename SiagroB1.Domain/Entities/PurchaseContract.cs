using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS")]
[Index("Code", "Sequence", IsUnique = true)]
public class PurchaseContract : BaseEntity
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
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("DeliveryLocation")]
    public required string DeliveryLocationCode { get; set; }
    public virtual Warehouse? DeliveryLocation { get; set; }
    
    [Column(TypeName = "NVARCHAR(MAX)")]
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
    [ForeignKey(nameof(LogisticRegion))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }
    
    public ICollection<PurchaseContractPriceFixation> PriceFixations { get; set; } = [];
    
    public ICollection<PurchaseContractTax> Taxes { get; set; } = [];
    
    public ICollection<PurchaseContractQualityParameter>  QualityParameters { get; set; } = [];
    
    public ICollection<ShipmentRelease> ShipmentReleases { get; set; } = [];

    public decimal FixedVolume => 
        decimal.Round(PriceFixations?
                .Where(x => x.Status == PriceFixationStatus.Confirmed)
                .Sum(x => x.FixationVolume ) ?? 0, 
            2, 
            MidpointRounding.ToEven) ;
    
    public decimal AvailableVolumeToPricing => TotalVolume - FixedVolume;
    
    public decimal TotalPrice => 
        decimal.Round(
            (PriceFixations?.Sum(x => x.FixationPrice * x.FixationVolume) ?? 0),
            2 , 
            MidpointRounding.ToEven) ;
    
    public decimal TotalTax => 
        decimal.Round((Taxes?.Sum(x => x.TotalTax) ?? 0), 2, MidpointRounding.ToEven);
    
    public decimal TotalShipmentReleases =>
        decimal.Round(
            (ShipmentReleases?
                .Where(x => 
                    x.Status is ReleaseStatus.Approved or
                            ReleaseStatus.Completed)
                .Sum(x => x.ReleasedQuantity) ?? 0),
        2, MidpointRounding.ToEven);
    
    public decimal TotalAvailableToRelease => 
        decimal.Round(TotalVolume - TotalShipmentReleases, 2, MidpointRounding.ToEven);
    
    public bool HasShipmentReleases => ShipmentReleases
        .Any(x => x.Status != ReleaseStatus.Cancelled);
}