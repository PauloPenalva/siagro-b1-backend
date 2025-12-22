using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("DOC_NUMBERS")]
[Index("TransactionCode", "Name", IsUnique = true)]
public class DocNumber
{
    [Key]
    public Guid Key { get; set; }
    
    public TransactionCode TransactionCode { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    public int FirstNumber { get; set; } = 0;

    public int LastNumber { get; set; } = 0;

    public int NextNumber { get; set; } = 0;
    
    public bool Default { get; set; } = false;
    
    [Column(TypeName = "VARCHAR(50)")]
    public string? Prefix { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(50)")]
    public string? Suffix { get; set; } = string.Empty;
    
    [Column(TypeName = "VARCHAR(14)", Order = 1)]
    public string? BranchCode { get; set; }
    public virtual Branch? Branch { get; set; }  
    
    public bool Inactive { get; set; } = false;
    
    public bool IsManual { get; set; } = false;
    
    [Column(TypeName = "VARCHAR(2)")]
    public string? NumberSize { get; set; }
}