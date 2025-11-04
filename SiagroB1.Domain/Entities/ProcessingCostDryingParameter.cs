using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_cost_drying_parameters")]
    public class ProcessingCostDryingParameter : BaseEntity<string>
    {
        [Column("processing_cost_key")]
        [ForeignKey(nameof(ProcessingCost))]
        public string? ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column("initial_moisture")]
        public decimal? InitialMoisture { get; set; }
        
        [Column("final_moisture")]
        public decimal? FinalMoisture { get; set; }

        [Column("percentual")]
        public decimal? Percentual { get; set; }
    }
}