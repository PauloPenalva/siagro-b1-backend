using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_SCALES")]
[Index(nameof(Code), IsUnique = true)]
public class TruckScale
{
    [Key]
    public required string Code { get; set; }
    
    public required string Name { get; set; }
    
    public required string Localization { get; set; }
}