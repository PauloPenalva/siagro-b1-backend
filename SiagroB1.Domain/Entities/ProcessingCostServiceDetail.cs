using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_cost_service_detail")]
    public class ProcessingCostServiceDetail : BaseEntity<string>
    {
        [Column("processing_cost_key")]
        [ForeignKey(nameof(ProcessingCost))]
        public required string ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column("processing_service_key")]
        [ForeignKey(nameof(ProcessingService))]
        public string? ProcessingServiceKey { get; set; }
        public ProcessingService? ProcessingService { get; set; }

        [Column("price")]
        public decimal Price { get; set; }
    }
}