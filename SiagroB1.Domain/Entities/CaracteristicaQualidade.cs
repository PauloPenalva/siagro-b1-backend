using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities
{
    [Table("caracteristica_qualidade")]
    public class CaracteristicaQualidade : BaseEntity
    {
        [Column("descricao")]
        public required string Descricao { get; set; }

        [Column("nao_exibe")]
        public bool NaoExibe { get; set; } = false;

        [Column("ponto_execucao")]
        public required string PontoExecucao { get; set; }
    }
}