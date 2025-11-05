using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("PROCESSING_COST_SERVICE_DETAILS")]
    public class ProcessingCostServiceDetail : BaseEntity<string>
    {
        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        [ForeignKey(nameof(ProcessingCost))]
        public required string ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        [ForeignKey(nameof(ProcessingService))]
        public required string? ProcessingServiceKey { get; set; }
        public ProcessingService? ProcessingService { get; set; }

        [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
        public decimal Price { get; set; } = 0;
    }
}