using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("tabela_custo_secagem_valor")]
    public class TabelaCustoValorSecagem
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("tabela_custo_id")]
        [ForeignKey(nameof(TabelaCusto))]
        public int TabelaCustoId { get; set; }
        public TabelaCusto? TabelaCusto { get; set; }

        [Column("umidade_de")]
        public decimal? UmidadeDe { get; set; }
        
        [Column("umidade_ate")]
        public decimal? UmidadeAte { get; set; }

        [Column("valor")]
        public decimal? ValorCobranca { get; set; }
    }
}