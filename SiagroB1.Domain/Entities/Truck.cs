using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("TRUCKS")]
public class Truck : BaseEntity<string>
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? Model { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? City { get; set; }
    
    [Column(TypeName = "VARCHAR(10)")]
    [ForeignKey("State")]
    public string? StateKey { get; set; }
    public virtual State? State { get; set; }
}