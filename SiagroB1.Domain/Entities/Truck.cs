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
}