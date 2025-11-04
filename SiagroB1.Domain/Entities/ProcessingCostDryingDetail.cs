using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_cost_drying_details")]
    public class ProcessingCostDryingDetail : BaseEntity<string>
    {
        [Column("processing_cost_key")]
        [ForeignKey(nameof(ProcessingCost))]
        public string? ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column("initial_moisture")]
        public decimal? InitialMoisture { get; set; }
        
        [Column("final_moisture")]
        public decimal? FinalMoisture { get; set; }

        [Column("price")]
        public decimal? Price { get; set; }
    }
}