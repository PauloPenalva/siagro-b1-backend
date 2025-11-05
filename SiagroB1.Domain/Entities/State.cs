using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("STATES")]
public class State : BaseEntity<string>
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    [Column(TypeName = "VARCHAR(2) NOT NULL")]
    public required string Abbreviation { get; set; }
}