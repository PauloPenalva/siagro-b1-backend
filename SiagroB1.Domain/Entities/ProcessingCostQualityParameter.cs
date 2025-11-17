using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PROCESSING_COST_QUALITY_PARAMETERS")]
public class ProcessingCostQualityParameter
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int? ItemId { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey(nameof(ProcessingCost))]
    public string? ProcessingCostCode { get; set; }
    public virtual ProcessingCost? ProcessingCost { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("QualityAttrib")]
    public required string QualityAttribCode { get; set; }
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
