using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("ROLE_MENUS")]
public class RoleMenu
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Role { get; set; }
    
    public Guid MenuItemId { get; set; }
    
    public virtual MenuItem? MenuItem { get; set; }
}