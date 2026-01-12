using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_CONTRACTS")]
[Index("Code", IsUnique = true)]
public class SalesContract : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? Complement { get; set; }

    public DateTime? CreationDate { get; set; } = DateTime.Now;

    public ContractType Type { get; set; }
    
    public MarketType? MarketType { get; set; }
    
    public ContractStatus? Status { get; set; } = ContractStatus.Draft;
    
    public int? AgentCode { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? AgentName { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    public DateTime DeliveryStartDate { get; set; }

    public DateTime DeliveryEndDate { get; set; }

    public FreightTerms FreightTerms { get; set; }

    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")]  
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    [ForeignKey("UnitOfMeasureModel")]
    public required string UnitOfMeasureCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("HarvestSeason")]
    public required string HarvestSeasonCode { get; set; }
    public virtual HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TotalVolume { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(LogisticRegionCode))]
    public string? LogisticRegionCode { get; set; }
    public virtual LogisticRegion? LogisticRegion { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Volume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal Price { get; set; } = 0;
    
    public CurrencyType? StandardCurrency { get; set; } = CurrencyType.Brl;
    
    public DateTime? StandardCashFlowDate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? PaymentTerms { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? ApprovalComments { get; set; }
    
    public ICollection<SalesInvoiceItem>  SalesInvoiceItems { get; set; } = [];
    
    [NotMapped]
    public decimal TotalPrice => 
        decimal.Round((TotalVolume * Price), 2 , MidpointRounding.ToEven);

    [NotMapped]
    public decimal TotalVolumeOutgoing => SalesInvoiceItems?
        .Where(x => x.SalesInvoice?.InvoiceStatus is InvoiceStatus.Confirmed &&
                    x.SalesInvoice.InvoiceType == SalesInvoiceType.Normal)
        .Sum(x => x.Quantity) ?? 0;
    
    [NotMapped]
    public decimal TotalVolumeIncoming => SalesInvoiceItems?
        .Where(x => x.SalesInvoice?.InvoiceStatus is InvoiceStatus.Confirmed &&
                    x.SalesInvoice.InvoiceType == SalesInvoiceType.Return)
        .Sum(x => x.Quantity) ?? 0;
    
    [NotMapped]
    public decimal AvaiableVolume =>
        decimal.Round(TotalVolume - TotalVolumeOutgoing + TotalVolumeIncoming, 3, MidpointRounding.ToEven);
}