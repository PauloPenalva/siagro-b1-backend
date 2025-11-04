using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_cost_quality_parameter")]
    public class ProcessingCostQualityParameter : BaseEntity<string>
    {
        [Column("processing_cost_key")]
        [ForeignKey(nameof(ProcessingCost))]
        public string? ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column("quality_attrib_key")]
        [ForeignKey("QualityAttrib")]
        public string? QualityAttribKey { get; set; }
        public QualityAttrib? QualityAttrib { get; set; }

        [Column("max_limit")]
        public decimal? MaxLimit { get; set; }

        [Column("excess_discount_rate")]
        public decimal? ExcessDiscountRate { get; set; }   
    }
}