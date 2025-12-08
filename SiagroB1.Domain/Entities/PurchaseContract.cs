using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS")]
[Index("Code", IsUnique = true)]
public class PurchaseContract : BaseEntity
{
    [Column(TypeName = "VARCHAR(10)")]
    public string? DocTypeCode { get; set; }
    public virtual DocType? DocType { get; set; }
    
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? Complement { get; set; }

    public DateTime? CreationDate { get; set; } = DateTime.Now;
    
    public ContractType Type { get; set; }
    
    public MarketType? MarketType { get; set; }
    
    public ContractStatus? Status { get; set; } = ContractStatus.Draft;
    
    [Column(TypeName = "VARCHAR(10) NO NULL")]
    public string? AgentCode { get; set; }
    public virtual Agent? Agent { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string? CardName { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FreightCostStandard { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public string? FreightUmCode { get; set; }
    public virtual UnitOfMeasure? FreightUm { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]  
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string? ItemName { get; set; }

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
    
    [Column(TypeName = "DECIMAL(18,6) DEFAULT 0")]
    public decimal StandardPrice { get; set; }

    public CurrencyType? StandardCurrency { get; set; } = CurrencyType.Brl;
    
    public DateTime? StandardCashFlowDate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? PaymentTerms { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("DeliveryLocation")]
    public required string DeliveryLocationCode { get; set; }
    public virtual Warehouse? DeliveryLocation { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(LogisticRegion))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
    
    public ICollection<PurchaseContractPriceFixation> PriceFixations { get; set; } = [];
    
    public ICollection<PurchaseContractTax> Taxes { get; set; } = [];
    
    public ICollection<PurchaseContractQualityParameter>  QualityParameters { get; set; } = [];
    
    
    public ICollection<PurchaseContractBroker> Brokers { get; set; } = [];
    
    public ICollection<ShipmentRelease> ShipmentReleases { get; set; } = [];
    
    public ICollection<PurchaseContractAllocation> Allocations { get; set; } = [];

    public decimal TotalStandard =>
        decimal.Round(TotalVolume * StandardPrice, 2, MidpointRounding.ToEven);
    
    public decimal FixedVolume => 
        decimal.Round(PriceFixations?
                .Where(x => x.Status is PriceFixationStatus.Confirmed
                    or PriceFixationStatus.InApproval)
                .Sum(x => x.FixationVolume ) ?? 0, 
            2, 
            MidpointRounding.ToEven) ;
    
    public decimal AvailableVolumeToPricing => TotalVolume - FixedVolume;
    
    public decimal TotalPrice => 
        decimal.Round(
            (PriceFixations?
                .Where(x => x.Status is PriceFixationStatus.Confirmed 
                    or PriceFixationStatus.InApproval)
                .Sum(x => x.FixationPrice * x.FixationVolume) ?? 0),
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

    public decimal AvaiableVolume =>
        decimal.Round(
            TotalVolume - (Allocations?.Sum(x => x.Volume) ?? 0), 2, MidpointRounding.ToEven) ;
}