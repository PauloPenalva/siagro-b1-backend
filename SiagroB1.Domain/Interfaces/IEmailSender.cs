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
}
