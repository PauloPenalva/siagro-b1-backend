using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Infra.Email;

namespace SiagroB1.Application.Tests.NfeEmail;

public class SmtpEmailSenderTests
{
    private static readonly SmtpSettings Smtp = new("localhost", 1, false, "user", "segredo", "nfe@empresa.com", "Empresa");

    private static SmtpEmailSender Create(string? enabled)
    {
        var values = new Dictionary<string, string?>();
        if (enabled != null) values["Email:Enabled"] = enabled;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new SmtpEmailSender(configuration, NullLogger<SmtpEmailSender>.Instance);
    }

    [Fact]
    public async Task Disabled_globally_logs_and_returns_success()
    {
        // Porta 1 inexistente: se tentasse conectar, o resultado seria falha.
        var result = await Create("false").SendAsync(
            new EmailMessage(["a@x.com"], "Assunto", "<p>oi</p>", []), Smtp);

        Assert.True(result.Success);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Enabled_with_unreachable_server_returns_failure_without_throwing()
    {
        var result = await Create("true").SendAsync(
            new EmailMessage(["a@x.com"], "Assunto", "<p>oi</p>", []), Smtp);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.DoesNotContain("segredo", result.Error);
    }

    [Fact]
    public void Builds_mime_with_all_recipients_and_attachments()
    {
        var message = new EmailMessage(
            ["a@x.com", "b@y.com"], "NF-e", "<p>corpo</p>",
            [new EmailAttachment("chave-procNFe.xml", [1, 2, 3], "application/xml"),
             new EmailAttachment("DANFE-10.pdf", [4, 5], "application/pdf")]);

        var mime = SmtpEmailSender.BuildMime(message, Smtp);

        Assert.Equal(2, mime.To.Count);
        Assert.Equal("NF-e", mime.Subject);
        Assert.Equal("nfe@empresa.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal(["chave-procNFe.xml", "DANFE-10.pdf"], mime.Attachments.Select(a => a.ContentDisposition?.FileName).ToArray());
    }
}
