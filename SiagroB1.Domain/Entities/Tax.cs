using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("TAXES")]
public class Tax
{
    [Key]
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "DECIMAL(5,4) DEFAULT 0")]
    public decimal Rate { get; set; }
}