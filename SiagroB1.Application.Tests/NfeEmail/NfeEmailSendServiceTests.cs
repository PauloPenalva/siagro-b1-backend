using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.NfeEmail;

/// <summary>Reenvio manual da NF-e por e-mail pela nota, e histórico.</summary>
public class NfeEmailSendServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly FakeBackgroundJobClient _jobs = new();
    private readonly StubSettings _settings = new();

    private NfeEmailSendService Service() => new(
        _db, new NfeEmailOutboxService(_db, new ConfigurationBuilder().Build(), NullLogger<NfeEmailOutboxService>.Instance),
        new NfeEmailRecipientResolver(_db), _settings, _jobs, new FakeCompanyContext("0002"));

    private async Task<Guid> SeedSalesAsync(NfeStatus status = NfeStatus.Authorized)
    {
        var key = Guid.NewGuid();
        _db.Context.SalesInvoices.Add(new SalesInvoice { Key = key, CardCode = "C1", NfeStatus = status });
        await _db.SaveChangesAsync();
        return key;
    }

    [Fact]
    public async Task Manual_send_creates_row_and_enqueues()
    {
        var key = await SeedSalesAsync();

        var row = await Service().SendAsync(NfeEmailDocumentType.Sales, key, ["a@x.com.br", " b@x.com.br "], "tester");

        var saved = await _db.Context.NfeEmailMessages.AsNoTracking().SingleAsync();
        Assert.Equal(row.Key, saved.Key);
        Assert.Equal(NfeEmailTrigger.Manual, saved.Trigger);
        Assert.Equal(NfeEmailEventType.Authorized, saved.EventType);
        Assert.Equal("a@x.com.br;b@x.com.br", saved.Recipients);
        Assert.NotNull(saved.EnqueuedAt);
        Assert.Equal(row.Key, Assert.Single(_jobs.EnqueuedOutboxKeys));
        Assert.Equal(["0002"], _jobs.EnqueuedCompanyCodes);
    }

    [Fact]
    public async Task Cancelled_invoice_sends_cancelled_event()
    {
        var key = await SeedSalesAsync(NfeStatus.Cancelled);

        var row = await Service().SendAsync(NfeEmailDocumentType.Sales, key, ["a@x.com.br"], "tester");

        Assert.Equal(NfeEmailEventType.Cancelled, row.EventType);
    }

    [Fact]
    public async Task Rejects_pending_invoice()
    {
        var key = await SeedSalesAsync(NfeStatus.Processing);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SendAsync(NfeEmailDocumentType.Sales, key, ["a@x.com.br"], "tester"));

        Assert.Equal("Só é possível enviar NF-e autorizada ou cancelada.", e.Message);
        Assert.Empty(_jobs.Enqueued);
    }

    [Fact]
    public async Task Rejects_third_party()
    {
        var key = Guid.NewGuid();
        _db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = key, CardCode = "F1", NfeStatus = NfeStatus.Authorized, IssuerType = DocumentIssuerType.ThirdParty,
        });
        await _db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SendAsync(NfeEmailDocumentType.Purchase, key, ["a@x.com.br"], "tester"));

        Assert.Equal("Esta nota não foi emitida pela empresa.", e.Message);
    }

    [Fact]
    public async Task Own_purchase_is_sent()
    {
        var key = Guid.NewGuid();
        _db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = key, CardCode = "F1", NfeStatus = NfeStatus.Authorized, IssuerType = DocumentIssuerType.Own,
        });
        await _db.SaveChangesAsync();

        var row = await Service().SendAsync(NfeEmailDocumentType.Purchase, key, ["a@x.com.br"], "tester");

        Assert.Equal(NfeEmailDocumentType.Purchase, row.DocumentType);
    }

    [Fact]
    public async Task Rejects_without_settings()
    {
        var key = await SeedSalesAsync();
        _settings.Smtp = null;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SendAsync(NfeEmailDocumentType.Sales, key, ["a@x.com.br"], "tester"));

        Assert.Contains("Configure o envio de e-mail da empresa", e.Message);
        Assert.Empty(await _db.Context.NfeEmailMessages.ToListAsync());
    }

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a b@x.com.br")]
    public async Task Rejects_invalid_recipient(string email)
    {
        var key = await SeedSalesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SendAsync(NfeEmailDocumentType.Sales, key, ["ok@x.com.br", email], "tester"));

        Assert.Contains("E-mail de NF-e inválido", e.Message);
        Assert.Empty(_jobs.Enqueued);
    }

    [Fact]
    public async Task Rejects_empty_recipients_and_duplicates()
    {
        var key = await SeedSalesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service().SendAsync(NfeEmailDocumentType.Sales, key, [], "t"));
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service().SendAsync(NfeEmailDocumentType.Sales, key, ["a@x.com.br", "A@x.com.br"], "t"));
    }

    [Fact]
    public async Task Default_recipients_come_from_partner()
    {
        var key = await SeedSalesAsync();
        _db.Context.BusinessPartnerNfeEmails.Add(new BusinessPartnerNfeEmail { CardCode = "C1", Email = "p@x.com.br", LineNum = 0 });
        await _db.SaveChangesAsync();

        Assert.Equal(["p@x.com.br"], await Service().DefaultRecipientsAsync(NfeEmailDocumentType.Sales, key));
    }

    private sealed class StubSettings : IEmailSettingsService
    {
        public SmtpSettings? Smtp { get; set; } = new("smtp.exemplo.com.br", 587, true, null, null, "nfe@ceagui.com.br", "CEAGUI");
        public Task<SmtpSettings?> GetSmtpAsync() => Task.FromResult(Smtp);
        public Task<EmailSettingsModel?> GetAsync() => throw new NotSupportedException();
        public Task<EmailSettingsModel> SaveAsync(EmailSettingsModel model, string userName) => throw new NotSupportedException();
        public Task<EmailSendResult> SendTestAsync(string to) => throw new NotSupportedException();
    }
}
