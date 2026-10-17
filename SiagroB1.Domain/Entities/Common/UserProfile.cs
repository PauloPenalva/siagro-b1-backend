using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("USERS_PROFILES")]
[Index(nameof(UserId), nameof(ProfileCode), nameof(CompanyCode), IsUnique = true)]
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

    /// <summary>
    /// Empresa em que o perfil vale. Ter ao menos um perfil numa empresa é o que autoriza o login nela
    /// (admin passa por cima). Anulável no schema porque é o bootstrap do startup — não a migration —
    /// que conhece a empresa padrão; os serviços sempre preenchem.
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    [ForeignKey(nameof(Company))]
    public string? CompanyCode { get; set; }

    public virtual Company? Company { get; set; }
}