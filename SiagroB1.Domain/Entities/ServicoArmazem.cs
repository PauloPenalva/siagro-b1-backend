using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("servicos")]
    public class ServicoArmazem : BaseEntity
    {
        [Column("descricao")]
        public required string Descricao { get; set; }

    }
}