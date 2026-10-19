using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.NfeEmail;

/// <summary>Configuração do remetente SMTP da empresa.</summary>
public class EmailSettingsServiceTests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static IConfiguration Config() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Nfe:CertificateKey"] = Key }).Build();

    private static (EmailSettingsService Service, SiagroB1.Infra.UnitOfWork Db, FakeEmailSender Sender) Create()
    {
        var db = TestDb.CreateUnitOfWork();
        var sender = new FakeEmailSender();
        return (new EmailSettingsService(db, Config(), sender), db, sender);
    }

    private static EmailSettingsModel Model(string? password = "segredo") => new()
    {
        Host = "smtp.exemplo.com.br", Port = 587, UseStartTls = true, UserName = "user",
        Password = password, FromAddress = "nfe@exemplo.com.br", FromName = "CEAGUI", AutoSendEnabled = true,
    };

    [Fact]
    public async Task Save_encrypts_password_and_get_never_returns_it()
    {
        var (service, db, _) = Create();

        await service.SaveAsync(Model(), "tester");
        var got = await service.GetAsync();

        Assert.NotNull(got);
        Assert.True(got!.HasPassword);
        Assert.Null(got.Password);
        var row = await db.Context.EmailSettings.AsNoTracking().SingleAsync();
        Assert.NotNull(row.PasswordEncrypted);
        Assert.DoesNotContain("segredo", System.Text.Encoding.UTF8.GetString(row.PasswordEncrypted!));
        Assert.Equal("tester", row.UpdatedBy);
    }

    [Fact]
    public async Task Save_without_password_keeps_previous()
    {
        var (service, _, _) = Create();
        await service.SaveAsync(Model("primeira"), "tester");

        await service.SaveAsync(Model(null), "tester");
        await service.SaveAsync(Model(""), "tester");

        var smtp = await service.GetSmtpAsync();
        Assert.Equal("primeira", smtp!.Password);
    }

    [Fact]
    public async Task Save_requires_host_and_from_address()
    {
        var (service, _, _) = Create();
        var model = Model();
        model.Host = " ";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(model, "tester"));
        Assert.Equal("Informe o servidor SMTP e o e-mail do remetente.", ex.Message);

        model = Model();
        model.FromAddress = "";
        await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(model, "tester"));
    }

    [Fact]
    public async Task GetSmtp_decrypts()
    {
        var (service, _, _) = Create();
        await service.SaveAsync(Model(), "tester");

        var smtp = await service.GetSmtpAsync();

        Assert.Equal(("smtp.exemplo.com.br", 587, true, "user", "segredo", "nfe@exemplo.com.br", "CEAGUI"),
            (smtp!.Host, smtp.Port, smtp.UseStartTls, smtp.UserName, smtp.Password, smtp.FromAddress, smtp.FromName));
    }

    [Fact]
    public async Task GetSmtp_is_null_when_not_configured()
    {
        var (service, _, _) = Create();
        Assert.Null(await service.GetSmtpAsync());
        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task SendTest_returns_smtp_error_message()
    {
        var (service, _, sender) = Create();
        await service.SaveAsync(Model(), "tester");
        sender.FailWith("Falha de autenticação");

        var result = await service.SendTestAsync("dest@exemplo.com.br");

        Assert.False(result.Success);
        Assert.Equal("Falha de autenticação", result.Error);
        var (message, _) = Assert.Single(sender.SentMessages);
        Assert.Equal("Teste de envio de e-mail - SIAGRO B1", message.Subject);
        Assert.Empty(message.Attachments);
    }

    [Fact]
    public async Task SendTest_without_settings_asks_to_configure()
    {
        var (service, _, _) = Create();
        var result = await service.SendTestAsync("dest@exemplo.com.br");
        Assert.False(result.Success);
        Assert.Contains("Configure", result.Error);
    }
}
