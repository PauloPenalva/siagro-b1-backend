using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("HARVEST_SEASSONS")]
public class HarvestSeason 
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Required(ErrorMessage = "Description is mandatory.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    public bool Inactive { get; set; } = false;
}