using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("tabela_custos")]
    public class TabelaCusto : BaseEntity
    {   
        [Required(ErrorMessage = "Descrição é obrigatório.")]
        [Column("descricao")]
        public required string Descricao { get; set; }
        
        [Required(ErrorMessage = "Taxa Armazenagem é obrigatório.")]
        [Range(0, double.MaxValue, ErrorMessage = "Informe o valor da taxa de armazenagem.")]
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

        public ICollection<TabelaCustoDescontoSecagem> DescontosSecagem { get; set; } = [];

        public ICollection<TabelaCustoValorSecagem> ValoresSecagem { get; set; } = [];

        public ICollection<TabelaCustoQualidade> Qualidades { get; set; } = [];

        public ICollection<TabelaCustoServico> Servicos { get; set; } = [];
    }
}