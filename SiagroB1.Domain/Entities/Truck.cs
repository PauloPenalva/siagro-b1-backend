using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("TRUCKS")]
public class Truck 
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? Model { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? City { get; set; }
    
    [Column(TypeName = "VARCHAR(2)")]
    [ForeignKey("State")]
    public string? StateKey { get; set; }
    public virtual State? State { get; set; }

    /// <summary>
    /// Tara do veículo em quilos. Nula de propósito: torná-la obrigatória travaria a gravação dos
    /// caminhões já cadastrados sem tara. Quem a cobra é a validação da pesagem, e só quando a
    /// balança tem <see cref="TruckScale.ValidateTare"/> ligado.
    /// </summary>
    public int? TareWeight { get; set; }
}