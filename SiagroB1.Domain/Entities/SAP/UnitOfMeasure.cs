using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OUOM")]
public class UnitOfMeasure 
{
    [Key]
    [Column("UomCode")]
    public required string Code { get; set; }
    
    [Column("UomName")]
    public required string Description { get; set; }
    
    [Column(TypeName = "VARCHAR(1)")]
    public string? Locked {get; set;}
}
