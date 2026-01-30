using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("OWNERSHIP_TRANSFER")]
public class OwnershipTransfer : DocumentEntity
{
    [Column(TypeName = "VARCHAR(50)")] 
    public string? TransferCode { get; set; } = string.Empty;
    
    public DateTime? Date { get; set; } = DateTime.Now.Date;
    
    public OwnershipTransferStatus? TransferStatus { get; set; } =  OwnershipTransferStatus.Open; 
    
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public string?  ItemName { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressOriginCode { get; set; }
    public virtual StorageAddress? StorageAddressOrigin { get; set; }
    
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string StorageAddressDestinationCode { get; set; }
    public virtual StorageAddress? StorageAddressDestination { get; set; }

    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string UomCode { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
}