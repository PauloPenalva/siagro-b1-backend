using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_ADDRESSES")]
[Index("Code", IsUnique = true)]
public class StorageAddress : BaseEntity
{
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Code { get; set; }
    
    public required DateTime CreationDate { get; set; }
    
    public StorageOwnershipType OwnershipType { get; set; }  
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string CardCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string ItemCode { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string WarehouseCode { get; set; }
    
    public Guid? PurchaseContractKey  { get; set; }
    
    public virtual ICollection<StorageTransaction> Transactions { get; set; } = [];

    public TransactionCode? TransactionOrigin { get; set; }

    public decimal TotalReceipt => Transactions
        .Where(x => x.TransactionType is StorageTransactionType.Receipt or 
            StorageTransactionType.ShipmentReleased && 
                    x.TransactionStatus is StorageTransactionsStatus.Confirmed or 
                    StorageTransactionsStatus.Invoiced)
        .Sum(x => x.Volume);
    
    public decimal TotalShipment => Transactions
        .Where(x => x.TransactionType is 
            StorageTransactionType.Shipment or 
            StorageTransactionType.SalesShipment && 
                    x.TransactionStatus is StorageTransactionsStatus.Confirmed or 
                    StorageTransactionsStatus.Invoiced)
        .Sum(x => x.Volume);
    
    public decimal TotalQualityLoss => Transactions
        .Where(x => x.TransactionType is StorageTransactionType.QualityLoss && 
                    x.TransactionStatus is StorageTransactionsStatus.Confirmed or StorageTransactionsStatus.Invoiced)
        .Sum(x => x.Volume);
    
    public decimal Balance => TotalReceipt - (TotalShipment + TotalQualityLoss);
}