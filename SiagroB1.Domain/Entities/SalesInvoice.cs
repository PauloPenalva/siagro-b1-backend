using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SALES_INVOICES")]
public class SalesInvoice : DocumentEntity
{
    [Column(TypeName = "VARCHAR(9)")] 
    public string? InvoiceNumber { get; set; } = string.Empty;

    public DateTime? InvoiceDate { get; set; } = DateTime.Now.Date;

    public InvoiceStatus? InvoiceStatus { get; set; }
    
    public SalesInvoiceType  InvoiceType { get; set; } = SalesInvoiceType.Normal;
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; } = string.Empty;  
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(15)")]
    public string? DeliveryCardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? DeliveryCardName { get; set; } = string.Empty; 
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public string? TruckingCompanyCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? TruckingCompanyName { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; } = string.Empty;
    
    public FreightTerms FreightTerms { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FreightCostStandard { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    /// <summary>
    /// Numero da Nota Fiscal
    /// </summary>
    [Column(TypeName = "VARCHAR(9)")]
    public string? TaxDocumentNumber  { get; set; }
    
    /// <summary>
    /// Serie da Nota Fiscal
    /// </summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? TaxDocumentSeries { get; set; }

    /// <summary>
    /// Chave da NF-e
    /// </summary>
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }
    
    /// <summary>
    /// Informações do Contribuinte
    /// </summary>
    [Column(TypeName = "VARCHAR(500) DEFAULT ''")]
    public string? TaxPayerComments { get; set; }
    
    /// <summary>
    /// Informações do Interesse do Fisco
    /// </summary>
    [Column(TypeName = "VARCHAR(500) DEFAULT ''")]
    public string? TaxComments { get; set; }
    
    public SalesInvoiceDeliveryStatus DeliveryStatus { get; set; } = SalesInvoiceDeliveryStatus.Open;
    
    public DateTime? DeliveryDate { get; set; }
    
    public ICollection<SalesInvoiceItem> Items { get; set; } = [];
    
    public ICollection<StorageTransaction> SalesTransactions { get; set; } = [];
    
    public Guid? SalesInvoiceOriginKey { get; set; }
    public SalesInvoice? SalesInvoiceOrigin { get; set; }
    
    [NotMapped]
    public decimal TotalInvoiceItems => Items.Sum(i => i.Total);
    
    public void AddItem(SalesInvoiceItem item)
    {
        item.SalesInvoice = this;
        Items.Add(item);
    }
}