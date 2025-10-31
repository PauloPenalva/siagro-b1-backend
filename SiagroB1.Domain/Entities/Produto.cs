using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("produto")]
    public class Produto : BaseEntity<int>
    {
        [Column("descricao")]
        public string Descricao { get; set; } = null!;
        
        [Column("unidade_codigo")]
        public required string UnidadeMedidaCodigo { get; set; }

        [ForeignKey("UnidadeMedidaCodigo")]
        public UnidadeMedida? UnidadeMedida { get; set; }

        [Column("unidade2_codigo")]
        public string? UnidadeMedida2Codigo { get; set; }

        [ForeignKey("UnidadeMedida2Codigo")]
        public UnidadeMedida? UnidadeMedida2 { get; set; }

        [Column("ncm")]
        public string? Ncm { get; set; }
    }
}