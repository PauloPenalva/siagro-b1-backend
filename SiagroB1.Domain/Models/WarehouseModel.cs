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
    
    public string? FName { get; set; }
    
    public string? Notes { get; set; }
    
    public string? City { get; set; }
    
    public string? State { get; set; }
}