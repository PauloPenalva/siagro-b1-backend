using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;
    
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

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal ShippedQuantity { get; set; }

    /// <summary>
    /// Token de concorrência otimista (SQL Server rowversion).
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Saldo disponível para romanear, derivado de <see cref="ShippedQuantity"/>
    /// (persistido, recalculado nos hooks de romaneio). Não depende de navegação.
    /// </summary>
    [NotMapped]
    public decimal AvailableQuantity =>
        Status != ReleaseStatus.Cancelled
            ? decimal.Round(ReleasedQuantity - ShippedQuantity, 3, MidpointRounding.ToEven)
            : decimal.Zero;
    
    [NotMapped]
    public bool HasStorageTransactions => Transactions
        .Any(x => 
            x.TransactionStatus is not StorageTransactionsStatus.Cancelled &&
            x.TransactionType is StorageTransactionType.SalesShipment or 
                StorageTransactionType.SalesShipmentReturn or 
                StorageTransactionType.Purchase or 
                StorageTransactionType.PurchaseReturn);
}