using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Remetente SMTP da empresa (só STANDALONE). Linha única (<see cref="Key"/> = 1) por banco de empresa.
/// A senha fica cifrada (AES-GCM, chave Nfe:CertificateKey) e nunca sai pela API.
/// </summary>
[Table("EMAIL_SETTINGS")]
public class EmailSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Key { get; set; } = 1;

    [Column(TypeName = "VARCHAR(200)")]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseStartTls { get; set; } = true;

    [Column(TypeName = "VARCHAR(200)")]
    public string? UserName { get; set; }

    [Column(TypeName = "VARBINARY(512)")]
    public byte[]? PasswordEncrypted { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string FromAddress { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string? FromName { get; set; }

    public bool AutoSendEnabled { get; set; }

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;
}
