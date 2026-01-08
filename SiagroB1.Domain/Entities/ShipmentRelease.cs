using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

// ReSharper disable All

namespace SiagroB1.Domain.Entities;

[Table("SHIPMENT_RELEASES")]
public class ShipmentRelease : DocumentEntity
{
    public required Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    public DateTime ReleaseDate { get; set; } = DateTime.Now.Date;

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReleasedQuantity { get; set; } // Quantidade liberada

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string DeliveryLocationCode { get; set; }
    
    [Column(TypeName = "VARCHAR(200)")]
    public string? DeliveryLocationName { get; set; }
    
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Pending;
    
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
                .Sum(x => x.GrossWeight) ?? decimal.Zero
            : decimal.Zero;
    
    public bool HasStorageTransactions => Transactions
        .Any(x => 
            x.TransactionStatus is not StorageTransactionsStatus.Cancelled &&
            x.TransactionType is StorageTransactionType.SalesShipment or StorageTransactionType.SalesShipmentReturn);
}