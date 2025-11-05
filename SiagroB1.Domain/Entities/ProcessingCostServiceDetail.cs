using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("PROCESSING_COST_SERVICE_DETAILS")]
    public class ProcessingCostServiceDetail 
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ItemId { get; set; }
        
        [Column(TypeName = "VARCHAR(10)")]
        [ForeignKey(nameof(ProcessingCost))]
        public string? ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }

        [Column(TypeName = "VARCHAR(10) NOT NULL")]
        [ForeignKey(nameof(ProcessingService))]
        public required string? ProcessingServiceKey { get; set; }
        public ProcessingService? ProcessingService { get; set; }

        [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
        public decimal Price { get; set; } = 0;
    }
}