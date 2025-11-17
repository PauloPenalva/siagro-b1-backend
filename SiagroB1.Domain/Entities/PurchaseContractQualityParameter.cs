using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PURCHASE_CONTRACTS_QUALITY_PARAMETERS")]
public class PurchaseContractQualityParameter
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    [ForeignKey(nameof(PurchaseContract))]
    public Guid? PurchaseContractKey { get; set; }
    public virtual PurchaseContract? PurchaseContract { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(QualityAttrib))]
    public required string  QualityAttribCode { get; set; }
    public virtual QualityAttrib? QualityAttrib { get; set; }
    
    [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
    public decimal MaxLimitRate { get; set; }
}