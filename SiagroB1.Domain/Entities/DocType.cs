using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("DOC_TYPES")]
public class DocType
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public TransactionCode TransactionCode { get; set; }
    
    [Column(TypeName = "VARCHAR(3) NOT NULL")]
    public required string Serie { get; set; }

    public int FirstNumber { get; set; } = 0;

    public int LastNumber { get; set; } = 0;

    public int NextNumber { get; set; } = 0;
}