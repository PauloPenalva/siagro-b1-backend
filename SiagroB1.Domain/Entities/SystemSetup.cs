using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("SYSTEM_SETUP")]
[Index("Code", IsUnique = true)]
public class SystemSetup
{
    [Key]
    [Column(TypeName = "VARCHAR(4) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(4)")]
    public string? DefaultUoM { get; set; }
    
    public CurrencyType? DefaultCurrency { get; set; }
    
    [Column(TypeName = "VARCHAR(4)")]
    public string? DefaultFreightUoM { get; set; }

    public bool IsActive { get; set; } = true;
}