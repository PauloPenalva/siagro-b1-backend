using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SHIPMENT_RELEASES")]
[Index("ReleaseNumber", IsUnique = true)]
public class ShipmentRelease : BaseEntity
{
    [ForeignKey(nameof(PurchaseContractKey))]
    public required Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
    
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string ReleaseNumber { get; set; }

    public DateTime ReleaseDate { get; set; } = DateTime.Now.Date;
    
    public DateTime? ExpectedDeliveryDate { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal ReleasedQuantity { get; set; } // Quantidade liberada
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal AvailableQuantity { get; set; } // Saldo disponível para romaneio
    
    public ReleaseStatus Status { get; set; } = ReleaseStatus.Pending;
    
    // Rastreabilidade
    [Column(TypeName = "VARCHAR(50)")]
    public string? CreatedBy { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    [Column(TypeName = "VARCHAR(50)")]
    public string? ApprovedBy { get; set; }
    
    public DateTime? ApprovedAt { get; set; }
    
    public virtual ICollection<StorageTransaction> Transactions { get; } = [];

    public bool HasStorageTransactions => Transactions
        .Any(x => 
            x.TransactionStatus is StorageTransactionsStatus.Confirmed or StorageTransactionsStatus.Pending &&
            x.TransactionType == StorageTransactionType.ShipmentReleased);
}