using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("PROCESSING_COST_DRYING_DETAILS")]
    public class ProcessingCostDryingDetail
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ItemId { get; set; }
        
        [Column(TypeName = "VARCHAR(10)")]
        [ForeignKey(nameof(ProcessingCost))]
        public string? ProcessingCostKey { get; set; }
        public ProcessingCost? ProcessingCost { get; set; }
        
        /// <summary>
        /// % UMIDADE INICIAL
        /// </summary>
        [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
        public decimal? InitialMoisture { get; set; } = 0;
        
        /// <summary>
        /// % UMIDADE FINAL
        /// </summary>
        [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
        public decimal? FinalMoisture { get; set; } = 0;
        
        /// <summary>
        /// custo de secagem
        /// </summary>
        [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
        public decimal? Price { get; set; } = 0;
    }
}