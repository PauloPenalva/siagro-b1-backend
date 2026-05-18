using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("ROLE_PERMISSIONS")]
[Index(nameof(RoleCode), nameof(PermissionCode), IsUnique = true)]
public class RolePermission
{
    [Key]
    public Guid Id { get; set; } =  Guid.NewGuid();

    [ForeignKey(nameof(Role))]
    [Column(TypeName = "VARCHAR(50)")]
    public required string RoleCode { get; set; }
    
    public virtual Role? Role { get; set; }
    
    [ForeignKey(nameof(Permission))]
    [Column(TypeName = "VARCHAR(50)")]
    public required string PermissionCode { get; set; }
    
    public virtual Permission? Permission { get; set; }
}