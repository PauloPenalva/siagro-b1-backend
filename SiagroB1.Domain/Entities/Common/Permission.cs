using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("PERMISSIONS")]
public class Permission
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? Description { get; set; }
}