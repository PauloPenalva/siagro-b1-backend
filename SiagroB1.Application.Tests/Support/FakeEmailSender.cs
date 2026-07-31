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
}
