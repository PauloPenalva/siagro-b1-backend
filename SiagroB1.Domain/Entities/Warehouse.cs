using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("WAREHOUSES")]
public class Warehouse 
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Required(ErrorMessage = "Name is mandatory.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    /// <summary>
    /// Brazilian CNPJ
    /// </summary>
    [Column("TAXID", TypeName = "VARCHAR(14) NOT NULL")]
    public string? TaxId { get; set; }
    
    public bool Inactive { get; set; }

    public WarehouseType Type { get; set; } = WarehouseType.Owner;
}