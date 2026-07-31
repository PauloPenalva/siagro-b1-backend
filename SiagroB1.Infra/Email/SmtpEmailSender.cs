using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Infra.Email;

/// <summary>
/// Envio de e-mail por SMTP (MailKit).
///
/// Com <c>Email:Enabled = false</c> — o padrão em desenvolvimento — nada é enviado: o e-mail vai
/// para o log. Isso permite exercitar a recuperação de senha inteira, inclusive abrindo o link,
/// sem um servidor SMTP configurado.
/// </summary>
public class SmtpEmailSender(
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    /// <summary>Primeira URL do corpo, para o log destacá-la quando o envio está desligado.</summary>
    private static string? FirstLink(string htmlBody)
    {
        var match = System.Text.RegularExpressions.Regex.Match(htmlBody, @"https?://[^\s""'<>]+");

        return match.Success ? match.Value : null;
    }

    public async Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (!configuration.GetValue("Email:Enabled", false))
        {
            // Com o envio desligado o log é o único destino da mensagem. O link vai destacado numa
            // linha própria: enterrado no corpo HTML ele é impossível de achar no meio do tráfego,
            // e é justamente ele que destrava o primeiro acesso de quem veio do OUSR.
            logger.LogWarning(
                "ENVIO DE E-MAIL DESABILITADO (Email:Enabled = false) - nada foi entregue.\n" +
                "  Destinatário: {To}\n" +
                "  Assunto: {Subject}\n" +
                "  LINK: {Link}\n" +
                "{Body}",
                to, subject, FirstLink(htmlBody) ?? "(a mensagem não contém link)", htmlBody);
            return true;
        }

        var host = configuration["Email:Host"];
        var fromAddress = configuration["Email:FromAddress"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
        {
            logger.LogError("Email:Host ou Email:FromAddress não configurados - e-mail para {To} não enviado.", to);
            return false;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(configuration["Email:FromName"] ?? "SIAGRO B1", fromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            await client.ConnectAsync(
                host,
                configuration.GetValue("Email:Port", 587),
                configuration.GetValue("Email:UseStartTls", true)
                    ? SecureSocketOptions.StartTls
                    : SecureSocketOptions.Auto,
                ct);

            var user = configuration["Email:User"];
            if (!string.IsNullOrWhiteSpace(user))
            {
                await client.AuthenticateAsync(user, configuration["Email:Password"], ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            logger.LogInformation("E-mail enviado para {To}: {Subject}", to, subject);
            return true;
        }
        catch (Exception exception)
        {
            // Falha de SMTP não pode virar erro na tela: quem chama já responde de forma genérica.
            logger.LogError(exception, "Falha ao enviar e-mail para {To}.", to);
            return false;
        }
    }
}
