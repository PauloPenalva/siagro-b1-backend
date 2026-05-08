using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("PROFILES_ROLES")]
[Index(nameof(ProfileId), nameof(Role), IsUnique = true )]
public class ProfileRole
{
    [Key]
    public Guid Id { get; set; } =  Guid.NewGuid();

    public required Guid ProfileId { get; set; }
    
    public virtual Profile Profile { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public required string Role { get; set; }
}