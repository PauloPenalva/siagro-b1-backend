using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("WAREHOUSES")]
public class Warehouse : BaseEntity
{
    [Required(ErrorMessage = "Name is mandatory.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    [Column("TAXID", TypeName = "VARCHAR(14) NOT NULL")]
    public string? TaxId { get; set; }
    
    public bool Inactive { get; set; } = false;
}