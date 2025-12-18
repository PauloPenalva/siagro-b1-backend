using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
// ReSharper disable All

namespace SiagroB1.Domain.Entities;

[Table("SHIPMENT_RELEASES")]
public class ShipmentRelease
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int RowId { get; set; }

    public required Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public DateTime ReleaseDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReleasedQuantity { get; set; } // Quantidade liberada

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string DeliveryLocationCode { get; set; }

    public virtual Warehouse? DeliveryLocation { get; set; }

    // [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    // public decimal AvailableQuantity { get; set; } // Saldo disponível para romaneio

    public ReleaseStatus Status { get; set; } = ReleaseStatus.Pending;

    public DateTime? CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")] public string? CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100)")] public string? UpdatedBy { get; set; } = string.Empty;

    public DateTime? ApprovedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")] public string? ApprovedBy { get; set; } = string.Empty;

    public DateTime? CanceledAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")] public string? CanceledBy { get; set; } = string.Empty;

    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    /// <summary>
    /// saldo disponivel para romanear
    /// </summary>
    public decimal AvailableQuantity => 
        Status is not ReleaseStatus.Cancelled 
            ? ReleasedQuantity - Transactions?
                .Where(x => 
                    x.TransactionStatus is not StorageTransactionsStatus.Cancelled &&
                    x.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn
                    )
                .Sum(x => x.NetWeight) ?? decimal.Zero
            : decimal.Zero;
    
    public bool HasStorageTransactions => Transactions
        .Any(x => 
            x.TransactionStatus is not StorageTransactionsStatus.Cancelled &&
            x.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn);
}