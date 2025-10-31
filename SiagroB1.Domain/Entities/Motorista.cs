using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("motoristas")]
public class Motorista : BaseEntity<int>
{
    [Column("nome")]
    public required string Nome { get; set; }

    [Column("cpf")]
    public string? Cpf { get; set; }
}