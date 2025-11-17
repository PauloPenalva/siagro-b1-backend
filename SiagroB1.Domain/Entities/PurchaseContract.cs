using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Enums;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS")]
[Index("Code")]
public class PurchaseContract : BaseEntity
{
    [Column(TypeName = "VARCHAR(15)")]
    public string? Code { get; set; }
    
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
    
    public ICollection<PurchaseContractPriceFixation> PriceFixations { get; set; } = [];

    public ICollection<PurchaseContractTax> Taxes { get; set; } = [];
    
    public decimal FixedVolume => 
        decimal.Round(PriceFixations?.Sum(x => x.FixationVolume) ?? 0, 
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
}