using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.NfeEmail;

public interface IEmailSettingsService
{
    Task<EmailSettingsModel?> GetAsync();

    Task<EmailSettingsModel> SaveAsync(EmailSettingsModel model, string userName);

    /// <summary>Uso interno: SMTP com a senha aberta. Nunca exponha por API.</summary>
    Task<SmtpSettings?> GetSmtpAsync();

    Task<EmailSendResult> SendTestAsync(string to);
}

/// <summary>Remetente SMTP da empresa. Linha única; senha cifrada com a mesma chave do certificado.</summary>
public class EmailSettingsService(IUnitOfWork db, IConfiguration configuration, IEmailSender sender) : IEmailSettingsService
{
    private const int SingleKey = 1;

    public async Task<EmailSettingsModel?> GetAsync()
    {
        var row = await db.Context.EmailSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey);
        return row is null ? null : ToModel(row);
    }

    public async Task<EmailSettingsModel> SaveAsync(EmailSettingsModel model, string userName)
    {
        if (string.IsNullOrWhiteSpace(model.Host) || string.IsNullOrWhiteSpace(model.FromAddress))
            throw new DefaultException("Informe o servidor SMTP e o e-mail do remetente.");

        if (model.Port is < 1 or > 65535)
            throw new DefaultException("A porta SMTP vai de 1 a 65535.");

        var row = await db.Context.EmailSettings.FirstOrDefaultAsync(x => x.Key == SingleKey);

        if (row is null)
        {
            row = new EmailSettings { Key = SingleKey };
            db.Context.EmailSettings.Add(row);
        }

        row.Host = model.Host.Trim();
        row.Port = model.Port;
        row.UseStartTls = model.UseStartTls;
        row.UserName = string.IsNullOrWhiteSpace(model.UserName) ? null : model.UserName.Trim();
        row.FromAddress = model.FromAddress.Trim();
        row.FromName = string.IsNullOrWhiteSpace(model.FromName) ? null : model.FromName.Trim();
        row.AutoSendEnabled = model.AutoSendEnabled;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = userName;

        // Senha vazia mantém a atual; só cifra (e exige a chave do servidor) quando veio uma nova.
        if (!string.IsNullOrEmpty(model.Password))
            row.PasswordEncrypted = Cipher().Encrypt(model.Password);

        await db.SaveChangesAsync();

        return ToModel(row);
    }

    public async Task<SmtpSettings?> GetSmtpAsync()
    {
        var row = await db.Context.EmailSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == SingleKey);

        if (row is null) return null;

        var password = row.PasswordEncrypted is { Length: > 0 } ? Cipher().Decrypt(row.PasswordEncrypted) : null;

        return new SmtpSettings(row.Host, row.Port, row.UseStartTls, row.UserName, password, row.FromAddress, row.FromName);
    }

    public async Task<EmailSendResult> SendTestAsync(string to)
    {
        if (string.IsNullOrWhiteSpace(to))
            return new EmailSendResult(false, "Informe o e-mail de destino do teste.");

        SmtpSettings? smtp;

        try
        {
            smtp = await GetSmtpAsync();
        }
        catch (DefaultException e)
        {
            return new EmailSendResult(false, e.Message);
        }

        if (smtp is null)
            return new EmailSendResult(false, "Configure o envio de e-mail da empresa antes de testar.");

        var message = new EmailMessage(
            [to.Trim()],
            "Teste de envio de e-mail - SIAGRO B1",
            "<p>Este é um e-mail de teste do SIAGRO B1. Se você o recebeu, a configuração de envio está correta.</p>",
            []);

        return await sender.SendAsync(message, smtp);
    }

    private CertificatePasswordCipher Cipher() =>
        CertificatePasswordCipher.FromBase64(configuration[CertificatePasswordCipher.ConfigurationKey]);

    private static EmailSettingsModel ToModel(EmailSettings row) => new()
    {
        Host = row.Host,
        Port = row.Port,
        UseStartTls = row.UseStartTls,
        UserName = row.UserName,
        HasPassword = row.PasswordEncrypted is { Length: > 0 },
        FromAddress = row.FromAddress,
        FromName = row.FromName,
        AutoSendEnabled = row.AutoSendEnabled,
        UpdatedAt = row.UpdatedAt,
        UpdatedBy = row.UpdatedBy,
    };
}
