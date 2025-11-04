using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("processing_costs")]
    public class ProcessingCost : BaseEntity<string>
    {   
        [Required(ErrorMessage = "Descrição é obrigatório.")]
        [Column("descricao")]
        public required string Descricao { get; set; }
        
        [Column("taxa_armazenagem")]
        public decimal? TaxaArmazenagem { get; set; }

        [Column("carencia_armazenagem")]
        public int? CarenciaArmazenagem { get; set; }

        [Column("vencimento_armazenagem")]
        public int? VencimentoArmazenagem { get; set; }

        [Column("taxa_expurgo")]
        public decimal? TaxaExpurgo { get; set; }

        [Column("vencimento_expurgo")]
        public int? VencimentoExpurgo { get; set; }

        [Column("carencia_quebra_tecnica")]
        public int? CarenciaQuebraTecnica { get; set; }

        [Column("vencimento_quebra_tecnica")]
        public int? VencimentoQuebraTecnica { get; set; }

        [Column("taxa_quebra_tecnica")]
        public decimal? TaxaQuebraTecnica { get; set; }

        public ICollection<ProcessingCostDryingParameter> DryingParameters { get; set; } = [];

        public ICollection<ProcessingCostDryingDetail> DryingDetails { get; set; } = [];

        public ICollection<ProcessingCostQualityParameter> QualityParameters { get; set; } = [];

        public ICollection<ProcessingCostServiceDetail> ServiceDetails { get; set; } = [];
    }
}