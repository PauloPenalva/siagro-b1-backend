using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("STATES")]
public class State
{
    [Key]
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string Abbreviation { get; set; }
}