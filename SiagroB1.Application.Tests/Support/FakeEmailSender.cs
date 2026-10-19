using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Captura os e-mails "enviados" para inspeção nos testes. <see cref="ShouldSucceed"/> simula um
/// servidor SMTP fora do ar.
/// </summary>
public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = [];

    public bool ShouldSucceed { get; set; } = true;

    public Task<bool> SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.FromResult(ShouldSucceed);
    }

    public List<(EmailMessage Message, SmtpSettings Smtp)> SentMessages { get; } = [];

    public string? FailureError { get; private set; }

    /// <summary>Faz os próximos envios com <see cref="EmailMessage"/> falharem com o erro dado.</summary>
    public void FailWith(string error) => FailureError = error;

    public Task<EmailSendResult> SendAsync(EmailMessage message, SmtpSettings smtp, CancellationToken ct = default)
    {
        SentMessages.Add((message, smtp));
        return Task.FromResult(FailureError is null
            ? new EmailSendResult(true, null)
            : new EmailSendResult(false, FailureError));
    }
}
