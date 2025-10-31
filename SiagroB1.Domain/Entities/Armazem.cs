using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("armazens")]
public class Armazem : BaseEntity<int>
{
    [Required(ErrorMessage = "Descrição é obrigatório.")]
    [Column("descricao")]
    public required string Descricao { get; set; }
    
    [Column("cnpj")]
    public string? Cnpj { get; set; }

    [Column("inativo")] 
    public bool Inativo { get; set; } = false;
}