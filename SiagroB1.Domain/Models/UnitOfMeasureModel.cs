using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class UnitOfMeasureModel
{
    [Key]
    public required string Code { get; set; }
    
    public required string Description { get; set; }
    
    public string? Locked {get; set;}
}