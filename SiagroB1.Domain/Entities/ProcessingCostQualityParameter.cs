using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("PROCESSING_COST_QUALITY_PARAMETERS")]
public class ProcessingCostQualityParameter
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
    
    [ForeignKey(nameof(ProcessingCost))]
    public Guid? ProcessingCostKey { get; set; }
    public ProcessingCost? ProcessingCost { get; set; }

    [ForeignKey("QualityAttrib")]
    public required Guid QualityAttribKey { get; set; }
    public QualityAttrib? QualityAttrib { get; set; }
    
    /// <summary>
    /// % TOLERANCIA 
    /// </summary>
    [Column(TypeName = "DECIMAL(18,1) NOT NULL")]
    public decimal? MaxLimitRate { get; set; }

    /// <summary>
    /// % QUEBRA
    /// </summary>
    [Column(TypeName = "DECIMAL(18,1) NOT NULL")]
    public decimal? ExcessDiscountRate { get; set; }   
}
