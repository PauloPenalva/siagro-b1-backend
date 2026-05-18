using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("ROLES")]
public class Role
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Code { get; set; }
    
    public string? Description { get; set; }

    public virtual ICollection<Permission> Permissions { get; set; } = [];

    public virtual ICollection<RoleMenu> Menus { get; set; } = [];
}