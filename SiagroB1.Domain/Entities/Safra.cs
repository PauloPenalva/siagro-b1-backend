using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("safras")]
public class Safra : BaseEntity<int>
{
    [Required(ErrorMessage = "Descrição é obrigatorio.")]
    [Column("descricao")]
    public required string Descricao { get; set; }

    [Column("inativa")]
    public bool Inativa { get; set; } = false;
}