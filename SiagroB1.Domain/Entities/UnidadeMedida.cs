using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities
{
    [Table("unidade_medida")]
    public class UnidadeMedida
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Column("codigo")]
        public required string Id { get; set; }

        [Column("descricao")]
        public required string Descricao { get; set; }

        [Column("chave_integracao")]
        public string? ChaveIntegracao { get; set; }
    
    }
}