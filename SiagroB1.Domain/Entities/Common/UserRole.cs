using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("USER_ROLES")]
[Index(nameof(UserId), nameof(RoleId), IsUnique = true)]
public class UserRole
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    public virtual User User { get; set; }
    
    public Guid RoleId { get; set; }
    
    public virtual Role Role { get; set; }
}