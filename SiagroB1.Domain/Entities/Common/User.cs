using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("SYSTEM_USERS")]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(Username), IsUnique = true)]
public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    public required string Username { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(256)")]
    public required string PasswordHash { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string FullName { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(100)")]
    public string? Email { get; set; }

    public bool IsActive { get; set; } = true;
    
    public bool IsAdmin { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime? LastLoginAt { get; set; }
}