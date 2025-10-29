using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("tabela_custo_servico")]
    public class TabelaCustoServico
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("tabela_custo_id")]
        [ForeignKey(nameof(TabelaCusto))]
        public int TabelaCustoId { get; set; }
        public TabelaCusto? TabelaCusto { get; set; }

        [Column("servico_id")]
        [ForeignKey(nameof(ServicoArmazem))]
        public int ServicoId { get; set; }
        public ServicoArmazem? ServicoArmazem { get; set; }

        [Column("valor")]
        public decimal Valor { get; set; }

        [Column("ponto_execucao")]
        public string? PontoExecucao { get; set; }
    }
}