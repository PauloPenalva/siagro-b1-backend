using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("veiculos")]
public class Veiculo : BaseEntity<int>
{
    [Column("placa")]
    public required string Placa { get; set; }
    
    [Column("modelo")]
    public string? Modelo { get; set; }
    
    [Column("municipio")]
    public string? Municipio { get; set; }
    
    [Column("estado_codigo")]
    [ForeignKey("Estado")]
    public string? EstadoCodigo { get; set; }
    
    public virtual Estado? Estado { get; set; }
}