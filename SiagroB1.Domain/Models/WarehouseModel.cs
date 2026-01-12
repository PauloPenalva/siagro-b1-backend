using System.ComponentModel.DataAnnotations;

namespace SiagroB1.Domain.Models;

public class WarehouseModel
{
    [Key]
    public required string Code { get; set; }
    
    public required string Name { get; set; }
    
    /// <summary>
    /// Brazilian CNPJ
    /// </summary>
    public string? TaxId { get; set; }
}