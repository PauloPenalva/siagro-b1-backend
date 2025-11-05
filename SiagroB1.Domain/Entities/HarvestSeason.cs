using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("HARVEST_SEASSONS")]
public class HarvestSeason : BaseEntity<string>
{
    [Required(ErrorMessage = "Description is mandatory.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    public bool Inactive { get; set; } = false;
}