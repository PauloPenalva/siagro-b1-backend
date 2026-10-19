namespace SiagroB1.Domain.Models;

/// <summary>
/// Configuração de e-mail como a tela vê. <see cref="Password"/> só vai da tela para o servidor
/// (vazio = manter a atual); na leitura vem sempre nula e <see cref="HasPassword"/> diz se existe.
/// </summary>
public class EmailSettingsModel
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public bool HasPassword { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public bool AutoSendEnabled { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
