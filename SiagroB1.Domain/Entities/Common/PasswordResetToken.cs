using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

/// <summary>
/// Pedido de redefinição de senha.
///
/// O token em claro só existe no e-mail enviado ao usuário: aqui fica apenas o hash, para que
/// alguém com acesso de leitura ao banco não consiga assumir a conta de ninguém.
/// </summary>
[Table("PASSWORD_RESET_TOKENS")]
[Index(nameof(TokenHash))]
public class PasswordResetToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public virtual User? User { get; set; }

    /// <summary>SHA-256 do token, em base64. O token em claro nunca é gravado.</summary>
    [Column(TypeName = "VARCHAR(64)")]
    public required string TokenHash { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Preenchido no momento do uso - é o que torna o token de uso único.</summary>
    public DateTime? UsedAt { get; set; }

    [Column(TypeName = "VARCHAR(45)")]
    public string? RequestIp { get; set; }
}
