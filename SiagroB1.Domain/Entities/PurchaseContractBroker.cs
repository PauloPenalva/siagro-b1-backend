using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_BROKERS")]
public class PurchaseContractBroker
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
    
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
    
    /// <summary>
    /// SAP ENTITY
    /// </summary>
    [Column(TypeName = "VARCHAR(10) NOT NULL")] 
    public required string CardCode { get; set; }
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal Commission { get; set; }
    
    [Column(TypeName = "VARCHAR(500)")]
    public string? Comments { get; set; }
    
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public string? ComissionUmCode { get; set; }
}