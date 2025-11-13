using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("TRUCKS")]
[Index("BranchKey","LicensePlate", IsUnique = true)]
public class Truck : BaseEntity
{
    [Column("VARCHAR(7) NOT NULL")]
    public required string LicensePlate { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? Model { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? City { get; set; }
    
    [Column(TypeName = "VARCHAR(2)")]
    [ForeignKey("State")]
    public string? StateKey { get; set; }
    public virtual State? State { get; set; }
}