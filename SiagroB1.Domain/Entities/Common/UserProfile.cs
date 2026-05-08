using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("USERS_PROFILES")]
[Index(nameof(UserId), nameof(ProfileId), Name = "IX_USERS_PROFILE_ID", IsUnique = true)]
public class UserProfile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    public virtual User User { get; set; }
    
    public Guid ProfileId { get; set; }
    
    public virtual Profile Profile { get; set; }
}