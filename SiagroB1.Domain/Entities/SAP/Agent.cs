using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OSLP")]
public class Agent
{
    [Key]
    [Column("SlpCode")]
    public int Code { get; set; }
    
    [Column("SlpName", TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
    
    [Column("Locked", TypeName = "VARCHAR(1) DEFAULT 'N'")]
    public string? Inactive { get; set; }
}