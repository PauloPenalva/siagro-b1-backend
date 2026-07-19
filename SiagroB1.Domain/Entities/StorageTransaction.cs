using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS")]
public class StorageTransaction : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public string? Code { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(StorageAddress))]
    public string? StorageAddressCode { get; set; }
    public virtual StorageAddress? StorageAddress { get; set; }
    
    public DateTime? TransactionDate { get; set; } = DateTime.Now.Date;
    
    [Column(TypeName = "VARCHAR(20)")]
    public string? TransactionTime { get; set; } = DateTime.Now.TimeOfDay.ToString();
    
    public StorageTransactionType TransactionType { get; set; }

    public StorageTransactionsStatus TransactionStatus { get; set; } = StorageTransactionsStatus.Pending;
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UnitOfMeasureCode { get; set; }

    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal DryingDiscount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal DryingServicePrice { get; set; }

    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal CleaningDiscount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal CleaningServicePrice { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal OthersDicount { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal ShipmentPrice { get; set; }
    
    [Column(TypeName = "decimal(18,2) DEFAULT 0")]
    public decimal ReceiptServicePrice { get; set; }
    
    [Column(TypeName = "decimal(18,3) DEFAULT 0")]
    public decimal NetWeight  { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string WarehouseCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }
    
    public Guid? ShipmentReleaseKey  { get; set; }
    public virtual ShipmentRelease? ShipmentRelease { get; set; }
    
    public TransactionCode? TransactionOrigin { get; set; } 
    
    public Guid? ShippingOrderKey { get; set; }
    
    [Column(TypeName = "VARCHAR(11) NULL")]
    [ForeignKey(nameof(TruckDriver))]
    public string? TruckDriverCode { get; set; }
    public virtual TruckDriver? TruckDriver { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? TruckCode { get; set; }
    
    public Guid? WeighingTicketKey { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingCostCode { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }
    
    [Column(TypeName = "VARCHAR(9)")]
    public string? InvoiceNumber { get; set; }
    
    [Column(TypeName = "VARCHAR(3)")]
    public string? InvoiceSerie { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal InvoiceQty { get; set; }
    
    [Column(TypeName = "VARCHAR(44)")]
    public string? ChaveNFe { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal AvaiableVolumeToAllocate { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    public ICollection<StorageTransactionQualityInspection> QualityInspections { get; set; } = [];
    
    public Guid? SalesInvoiceKey { get; set; }
    public virtual SalesInvoice? SalesInvoice { get; set; }

    public Guid? OwnershipTransferKey { get; set; }
    public virtual OwnershipTransfer? OwnershipTransfer { get; set; }
    
    [Column(TypeName = "VARCHAR(1) DEFAULT 'N'")]
    public string? IsOwnershipTransfer { get; set; }
    
    public bool IsInvoiced { get; set; }

    public Guid? StorageInvoiceKey { get; set; }

    public DateTime? InvoicedAt { get; set; }
    
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightPrice { get; set; }
    
    public Guid? ReturnInvoiceKey { get; set; }

    public DateTime? ReturnedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ReturnedBy { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion). Protege o
    /// saldo alocável (<see cref="AvaiableVolumeToAllocate"/>) contra
    /// atualizações concorrentes de aloca/desaloca.
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Apenas romaneios da família Compra podem ser alocados a contratos.
    /// Espelha os tipos aceitos por PurchaseContractsAllocationCreateService.
    /// </summary>
    [NotMapped]
    public bool IsAllocatable => TransactionType is
        StorageTransactionType.Purchase or
        StorageTransactionType.PurchaseReturn or
        StorageTransactionType.PurchaseQtyComplement or
        StorageTransactionType.PurchasePriceComplement;

    /// <summary>
    /// Fonte única de verdade do saldo alocável: deriva de NetWeight menos o
    /// total já alocado (valores absolutos). Substitui os += / -= incrementais,
    /// eliminando drift — chamar após qualquer mudança de alocação.
    /// </summary>
    public void RecalculateAvailableVolume(decimal totalAllocated)
        => AvaiableVolumeToAllocate = IsAllocatable ? NetWeight - totalAllocated : decimal.Zero;
}