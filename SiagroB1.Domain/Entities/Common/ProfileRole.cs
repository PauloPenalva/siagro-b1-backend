using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("PROFILES_ROLES")]
[Index(nameof(ProfileCode), nameof(RoleCode), IsUnique = true )]
public class ProfileRole
{
    [Key]
    public Guid Id { get; set; } =  Guid.NewGuid();

    [ForeignKey(nameof(Profile))]
    [Column(TypeName = "VARCHAR(50)")]
    public required string ProfileCode { get; set; }
    
    public virtual Profile? Profile { get; set; }
    
    [ForeignKey(nameof(Role))]
    [Column(TypeName = "VARCHAR(50)")]
    public required string RoleCode { get; set; }
    
    public virtual Role? Role { get; set; }
}