using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.NfeEmail;

public class NfeEmailDispatchJobTests
{
    private const string CardCode = "C00010";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeEmailSender _sender = new();
    private readonly FakeDanfeProvider _danfe = new();
    private readonly StubEmailSettings _settings = new();

    private NfeEmailDispatchJob Job() => new(
        _db, new NfeEmailRecipientResolver(_db), _settings, _sender, _danfe, NullLogger<NfeEmailDispatchJob>.Instance);

    private async Task<NfeEmailMessage> SeedAsync(
        NfeEmailEventType evt = NfeEmailEventType.Authorized, NfeEmailTrigger trigger = NfeEmailTrigger.Automatic,
        string? recipients = null, NfeEmailStatus status = NfeEmailStatus.Pending, params string[] partnerEmails)
    {
        var invoiceKey = Guid.NewGuid();
        _db.Context.SalesInvoices.Add(new SalesInvoice { Key = invoiceKey, CardCode = CardCode });
        _db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoiceKey, Kind = NfeXmlKind.Authorized,
            Xml = NfeEmailTestXml.ProcNfe(), CreatedAt = DateTime.Now,
        });

        for (var i = 0; i < partnerEmails.Length; i++)
            _db.Context.BusinessPartnerNfeEmails.Add(new BusinessPartnerNfeEmail
            {
                CardCode = CardCode, Email = partnerEmails[i], LineNum = i,
            });

        var row = new NfeEmailMessage
        {
            DocumentType = NfeEmailDocumentType.Sales, DocumentKey = invoiceKey, EventType = evt, Trigger = trigger,
            Recipients = recipients, Status = status, CreatedAt = DateTime.Now.AddMinutes(-5), CreatedBy = "tester",
        };
        _db.Context.NfeEmailMessages.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private Task<NfeEmailMessage> ReloadAsync(Guid key) =>
        _db.Context.NfeEmailMessages.AsNoTracking().SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Sends_to_all_partner_emails()
    {
        var row = await SeedAsync(partnerEmails: ["fiscal@fazenda.com.br", "dono@fazenda.com.br"]);

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        var message = Assert.Single(_sender.SentMessages).Message;
        Assert.Equal(["fiscal@fazenda.com.br", "dono@fazenda.com.br"], message.To);
        Assert.Equal(2, message.Attachments.Count); // procNFe + DANFE
        Assert.Equal((NfeEmailDocumentType.Sales, row.DocumentKey, "0001"), Assert.Single(_danfe.Calls));
        var saved = await ReloadAsync(row.Key);
        Assert.Equal(NfeEmailStatus.Sent, saved.Status);
        Assert.NotNull(saved.SentAt);
        Assert.Equal("fiscal@fazenda.com.br;dono@fazenda.com.br", saved.Recipients);
    }

    [Fact]
    public async Task Manual_row_uses_recipient_snapshot()
    {
        var row = await SeedAsync(trigger: NfeEmailTrigger.Manual, recipients: "contador@x.com.br",
            partnerEmails: ["fiscal@fazenda.com.br"]);

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        Assert.Equal(["contador@x.com.br"], Assert.Single(_sender.SentMessages).Message.To);
    }

    [Fact]
    public async Task No_emails_marks_skipped()
    {
        var row = await SeedAsync();

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        Assert.Empty(_sender.SentMessages);
        var saved = await ReloadAsync(row.Key);
        Assert.Equal(NfeEmailStatus.Skipped, saved.Status);
        Assert.Equal("Parceiro sem e-mail de NF-e", saved.LastError);
    }

    [Fact]
    public async Task No_settings_marks_failed()
    {
        _settings.Smtp = null;
        var row = await SeedAsync(partnerEmails: ["fiscal@fazenda.com.br"]);

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        var saved = await ReloadAsync(row.Key);
        Assert.Equal(NfeEmailStatus.Failed, saved.Status);
        Assert.Equal("Configure o envio de e-mail da empresa", saved.LastError);
    }

    [Fact]
    public async Task Smtp_failure_records_error_and_throws_until_third_attempt()
    {
        var row = await SeedAsync(partnerEmails: ["fiscal@fazenda.com.br"]);
        _sender.FailWith("5.7.1 Relaying denied " + new string('x', 2000));

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await Assert.ThrowsAsync<ApplicationException>(() => Job().ExecuteAsync("0001", row.Key, CancellationToken.None));
            var pending = await ReloadAsync(row.Key);
            Assert.Equal(NfeEmailStatus.Pending, pending.Status);
            Assert.Equal(attempt, pending.AttemptCount);
        }

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        var saved = await ReloadAsync(row.Key);
        Assert.Equal(NfeEmailStatus.Failed, saved.Status);
        Assert.Equal(3, saved.AttemptCount);
        Assert.StartsWith("Falha no envio: 5.7.1 Relaying denied", saved.LastError);
        Assert.Equal(1000, saved.LastError!.Length);
        Assert.Null(saved.SentAt);
    }

    [Fact]
    public async Task Danfe_failure_counts_as_failed_attempt()
    {
        var row = await SeedAsync(partnerEmails: ["fiscal@fazenda.com.br"]);
        _danfe.Error = new HttpRequestException("Connection refused");

        await Assert.ThrowsAsync<ApplicationException>(() => Job().ExecuteAsync("0001", row.Key, CancellationToken.None));

        Assert.Empty(_sender.SentMessages);
        var saved = await ReloadAsync(row.Key);
        Assert.Equal(NfeEmailStatus.Pending, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Contains("DANFE", saved.LastError);
    }

    [Fact]
    public async Task Cancelled_attaches_cancellation_event()
    {
        var row = await SeedAsync(NfeEmailEventType.Cancelled, partnerEmails: ["fiscal@fazenda.com.br"]);
        _db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = row.DocumentKey, Kind = NfeXmlKind.CancellationEvent,
            Xml = NfeEmailTestXml.ProcEvent, CreatedAt = DateTime.Now,
        });
        await _db.SaveChangesAsync();

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        var message = Assert.Single(_sender.SentMessages).Message;
        Assert.Contains(message.Attachments, a => a.FileName.EndsWith("-procEventoNFe.xml"));
        Assert.EndsWith("Cancelada", message.Subject);
    }

    [Fact]
    public async Task Already_sent_is_noop()
    {
        var row = await SeedAsync(status: NfeEmailStatus.Sent, partnerEmails: ["fiscal@fazenda.com.br"]);

        await Job().ExecuteAsync("0001", row.Key, CancellationToken.None);

        Assert.Empty(_sender.SentMessages);
        Assert.Empty(_danfe.Calls);
    }

    private sealed class StubEmailSettings : IEmailSettingsService
    {
        public SmtpSettings? Smtp { get; set; } =
            new("smtp.exemplo.com.br", 587, true, null, null, "nfe@ceagui.com.br", "CEAGUI");

        public Task<SmtpSettings?> GetSmtpAsync() => Task.FromResult(Smtp);
        public Task<EmailSettingsModel?> GetAsync() => throw new NotSupportedException();
        public Task<EmailSettingsModel> SaveAsync(EmailSettingsModel model, string userName) => throw new NotSupportedException();
        public Task<EmailSendResult> SendTestAsync(string to) => throw new NotSupportedException();
    }
}

/// <summary>DANFE de teste: devolve um PDF fixo e registra as chamadas; <see cref="Error"/> simula o Reports fora do ar.</summary>
public sealed class FakeDanfeProvider : IDanfeProvider
{
    public List<(NfeEmailDocumentType Type, Guid Key, string Company)> Calls { get; } = [];
    public Exception? Error { get; set; }

    public Task<byte[]> GetPdfAsync(NfeEmailDocumentType type, Guid key, string companyCode, CancellationToken ct = default)
    {
        Calls.Add((type, key, companyCode));
        return Error is null ? Task.FromResult("%PDF-fake"u8.ToArray()) : Task.FromException<byte[]>(Error);
    }
}
