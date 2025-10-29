using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("tabela_custo_qualidade")]
    public class TabelaCustoQualidade
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("tabela_custo_id")]
        [ForeignKey(nameof(TabelaCusto))]
        public int? TabelaCustoId { get; set; }
        public TabelaCusto? TabelaCusto { get; set; }

        [Column("caracteristica_qualidade_id")]
        [ForeignKey("CaracteristicaQualidade")]
        public int CaracteristicaQualidadeId { get; set; }
        public CaracteristicaQualidade? CaracteristicaQualidade { get; set; }

        [Column("tolerancia")]
        public decimal? Tolerancia { get; set; }

        [Column("desconto")]
        public decimal? Desconto { get; set; }   
    }
}