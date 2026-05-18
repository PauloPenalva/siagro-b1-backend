using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("USERS_PROFILES")]
[Index(nameof(UserId), nameof(ProfileCode), IsUnique = true)]
public class UserProfile
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid UserId { get; set; }
    
    public virtual User? User { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    [ForeignKey(nameof(Profile))]
    public required string ProfileCode { get; set; }
    
    public virtual Profile? Profile { get; set; }
}