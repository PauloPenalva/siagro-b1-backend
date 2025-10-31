using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("conta_contabil")]
    public class ContaContabil : BaseEntity<int>
    {
        [Column("codigo")]
        public required string Codigo { get; set; }

        [Column("descricao")]
        public required string Descricao { get; set; }
    }
}