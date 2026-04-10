using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class ItemModel 
{
    [Key]
    public required string ItemCode { get; set; }

    public required string ItemName { get; set; }
    
    public short? ItmsGrpCod {get; set;}
    
    public string? Enabled { get; set; }
}