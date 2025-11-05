using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("PROCESSING_COST_QUALITY_PARAMETERS")]
    public class ProcessingCostQualityParameter : BaseEntity<string>
    {
        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        [ForeignKey(nameof(ProcessingCost))]
        public required string ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        [ForeignKey("QualityAttrib")]
        public required string QualityAttribKey { get; set; }
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
}