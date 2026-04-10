using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities;

[Table("UNITS_OF_MEASURE")]
public class UnitOfMeasure : IUoMEntity
{
    [Key]
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }
    
    [Column(TypeName = "VARCHAR(1)")]
    public string? Locked {get; set;}
}
