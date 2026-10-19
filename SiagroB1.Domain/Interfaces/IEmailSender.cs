using SiagroB1.Domain.Dtos.Email;

namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Envio de e-mail transacional.
///
/// Nunca lança por falha do servidor de e-mail: devolve <c>false</c> e loga. Quem chama decide o
/// que fazer — no caso da recuperação de senha, nada, porque a resposta ao usuário é genérica de
/// propósito e não pode revelar se o endereço existe.
/// </summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);

    /// <summary>
    /// Envia para vários destinatários, com anexos, usando o SMTP da empresa. Nunca lança: a falha
    /// volta em <see cref="EmailSendResult"/>. Respeita a trava global <c>Email:Enabled</c>.
    /// </summary>
    Task<EmailSendResult> SendAsync(EmailMessage message, SmtpSettings smtp, CancellationToken ct = default);
}
