using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("ROLES")]
public class Role
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Column(TypeName = "VARCHAR(100) NOT NULL UNIQUE")]
    public required string Name { get; set; }
}