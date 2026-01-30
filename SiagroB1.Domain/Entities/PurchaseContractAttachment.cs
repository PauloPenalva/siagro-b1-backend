using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_ATTACHMENTS")]
public class PurchaseContractAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL" )]
    public required string Description { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL" )]
    public required string FileName { get; set; }
    
    [Column(TypeName = "VARBINARY(MAX)" )]
    public required byte[] FileData { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL" )]
    public required string ContentType { get; set; }
    
    public DateTime? CreatedAt { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL" )]
    public string? CreatedBy { get; set; }
}