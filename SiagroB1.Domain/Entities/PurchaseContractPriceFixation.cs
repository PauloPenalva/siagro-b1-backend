using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_PRICE_FIXATIONS")]
public class PurchaseContractPriceFixation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
   
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }
    
    public DateTime? FixationDate { get; set; } = DateTime.Now;
    
    [Column(TypeName = "DECIMAL(18,2) DEFAULT 0")]
    public decimal FreightCost { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal FixationVolume { get; set; } = 0;

    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal FixationPrice { get; set; } = 0;

    public PriceFixationStatus Status { get; set; } = PriceFixationStatus.InApproval;
}