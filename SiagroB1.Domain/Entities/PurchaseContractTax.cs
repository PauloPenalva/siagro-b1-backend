using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_TAXES")]
public class PurchaseContractTax
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    [ForeignKey(nameof(PurchaseContract))]
    public required Guid PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    [Column(TypeName = "VARCHAR(5) NOT NULL")]
    [ForeignKey(nameof(Tax))]
    public required string TaxCode { get; set; }
    public virtual Tax? Tax { get; set; }
    
    public decimal TotalTax => 
        decimal.Round( ((PurchaseContract?.TotalPrice / 100) * Tax?.Rate) ?? 0, 2, MidpointRounding.ToEven) ;
}