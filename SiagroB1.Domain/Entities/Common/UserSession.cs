using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("USER_SESSIONS")]
public class UserSession
{
    [Key]
    [Column(TypeName = "VARCHAR(100)")]
    public string SessionId { get; set; } = string.Empty;

    public Guid UserId { get; set; }
    public virtual User? User { get; set; }

    [Column(TypeName = "VARCHAR(45)")]
    public string? IpAddress { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivityAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>
    /// Empresa escolhida no login. Sem FK de propósito: sessão é histórico e não pode impedir a
    /// exclusão de uma empresa. Nulo só em sessões anteriores ao recurso (o middleware cai na padrão).
    /// </summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string? CompanyCode { get; set; }
}