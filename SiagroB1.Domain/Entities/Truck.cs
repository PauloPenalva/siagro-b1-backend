using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("trucks")]
public class Truck : BaseEntity<string>
{
    [Column("model")]
    public string? Model { get; set; }
    
    [Column("city")]
    public string? City { get; set; }
    
    [Column("state_key")]
    [ForeignKey("State")]
    public string? StateKey { get; set; }
    
    public virtual State? State { get; set; }
}