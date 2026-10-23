using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Jobs;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Services.Transport;
using SiagroB1.Application.Tests.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;

namespace SiagroB1.Application.Tests.NfeEmail;

/// <summary>E-mail automático do CT-e (XML + DACTE): fila pelos ganchos e envio pelo job da fila da NF-e.</summary>
public class CteEmailTests : CteEventsTestBase
{
    private const string CteXml =
        """<cteProc xmlns="http://www.portalfiscal.inf.br/cte" versao="4.00"><CTe><infCte><emit><xNome>TRANSPORTADORA TESTE LTDA</xNome></emit></infCte></CTe></cteProc>""";

    private const string CancelXml = """<procEventoCTe xmlns="http://www.portalfiscal.inf.br/cte" versao="4.00"><eventoCTe/></procEventoCTe>""";

    private sealed class NoAverba : IAverbaClient
    {
        public Task<AverbaLoginResult> LoginAsync(NfeEnvironment e, string u, string p, string c, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<AverbaCteResult> SendCteAsync(NfeEnvironment e, string b, string x, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Smtp : IEmailSettingsService
    {
        public Task<SmtpSettings?> GetSmtpAsync() =>
            Task.FromResult<SmtpSettings?>(new("smtp.exemplo.com.br", 587, true, null, null, "nfe@x.com.br", "X"));
        public Task<EmailSettingsModel?> GetAsync() => throw new NotSupportedException();
        public Task<EmailSettingsModel> SaveAsync(EmailSettingsModel model, string userName) => throw new NotSupportedException();
        public Task<EmailSendResult> SendTestAsync(string to) => throw new NotSupportedException();
    }

    private readonly FakeEmailSender _sender = new();
    private readonly FakeDanfeProvider _dacte = new();

    private NfeEmailOutboxService Outbox() =>
        new(Db, NfeTestSeed.Config(), NullLogger<NfeEmailOutboxService>.Instance);

    private CteLifecycleHooks Hooks() => new(
        Db, NfeTestSeed.Config(), FinancialDocumentTestServices.Transport(Db, new FakeDocNumberSequenceService()),
        new CteEndorsementCancellationService(Db, NfeTestSeed.Config(), Numbers, new NoAverba(),
            NullLogger<CteEndorsementCancellationService>.Instance),
        NullLogger<CteLifecycleHooks>.Instance, Outbox());

    private NfeEmailDispatchJob Job() => new(
        Db, new NfeEmailRecipientResolver(Db), new Smtp(), _sender, _dacte, NullLogger<NfeEmailDispatchJob>.Instance);

    private async Task<Guid> SeedCteAsync(bool autoSend = true, params (CtePartyRole Role, string CardCode)[] parties)
    {
        if (autoSend)
            Db.Context.EmailSettings.Add(new EmailSettings
            {
                Key = 1, Host = "smtp.exemplo.com.br", FromAddress = "nfe@x.com.br", AutoSendEnabled = true, UpdatedBy = "t",
            });
        var key = await SeedAsync();
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        doc.TakerRole = 0;
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
            { TransportDocumentKey = key, Kind = CteXmlKind.Authorized, Xml = CteXml });
        foreach (var (role, card) in parties)
            Db.Context.TransportDocumentParties.Add(new TransportDocumentParty
                { TransportDocumentKey = key, Role = role, CardCode = card, Name = card });
        await Db.SaveChangesAsync();
        return key;
    }

    private void Email(string card, params string[] emails)
    {
        for (var i = 0; i < emails.Length; i++)
            Db.Context.BusinessPartnerNfeEmails.Add(new BusinessPartnerNfeEmail { CardCode = card, Email = emails[i], LineNum = i });
    }

    private async Task<NfeEmailMessage> EnqueueAsync(Guid key, NfeEmailEventType evt = NfeEmailEventType.Authorized)
    {
        await Outbox().AddAutomaticAsync(NfeEmailDocumentType.Transport, key, evt, "t");
        await Db.SaveChangesAsync();
        return await Db.Context.NfeEmailMessages.SingleAsync(x => x.EventType == evt);
    }

    [Fact]
    public async Task Authorized_cte_enqueues_email_once()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"));

        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);
        await Hooks().OnAuthorizedAsync(doc);
        await Hooks().OnAuthorizedAsync(doc); // consulta repetida

        var row = await Db.Context.NfeEmailMessages.AsNoTracking().SingleAsync();
        Assert.Equal(NfeEmailDocumentType.Transport, row.DocumentType);
        Assert.Equal(key, row.DocumentKey);
        Assert.Equal(NfeEmailEventType.Authorized, row.EventType);
        Assert.Equal(NfeEmailTrigger.Automatic, row.Trigger);
    }

    [Fact]
    public async Task Cancelled_cte_enqueues_cancellation_email()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"));
        var doc = await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key);

        await Hooks().OnCancelledAsync(doc);

        var row = await Db.Context.NfeEmailMessages.AsNoTracking().SingleAsync();
        Assert.Equal(NfeEmailEventType.Cancelled, row.EventType);
        Assert.Equal(NfeEmailDocumentType.Transport, row.DocumentType);
    }

    [Fact]
    public async Task Recipients_are_taker_sender_recipient_deduplicated()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"), (CtePartyRole.Recipient, "DEST"),
            (CtePartyRole.Receiver, "REC"));
        Email("REM", "Fiscal@Rem.com.br", "outro@rem.com.br");
        Email("DEST", "fiscal@rem.com.br", "dest@dest.com.br");
        Email("REC", "recebedor@x.com.br"); // recebedor não recebe o e-mail
        await Db.SaveChangesAsync();
        var row = await EnqueueAsync(key);

        await Job().ExecuteAsync("0001", row.Key);

        var sent = Assert.Single(_sender.SentMessages).Message;
        Assert.Equal(["Fiscal@Rem.com.br", "outro@rem.com.br", "dest@dest.com.br"], sent.To);
    }

    [Fact]
    public async Task Taker_without_email_still_sends_to_others()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"), (CtePartyRole.Recipient, "DEST"),
            (CtePartyRole.OtherTaker, "TOM"));
        (await Db.Context.TransportDocuments.SingleAsync()).TakerRole = 4;
        Email("DEST", "dest@dest.com.br");
        await Db.SaveChangesAsync();
        var row = await EnqueueAsync(key);

        await Job().ExecuteAsync("0001", row.Key);

        Assert.Equal(["dest@dest.com.br"], Assert.Single(_sender.SentMessages).Message.To);
        Assert.Equal(NfeEmailStatus.Sent, (await Db.Context.NfeEmailMessages.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task No_recipient_is_recorded_like_nfe()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"));
        var row = await EnqueueAsync(key);

        await Job().ExecuteAsync("0001", row.Key);

        Assert.Empty(_sender.SentMessages);
        var saved = await Db.Context.NfeEmailMessages.AsNoTracking().SingleAsync();
        Assert.Equal(NfeEmailStatus.Skipped, saved.Status);
        Assert.Equal("Parceiro sem e-mail de NF-e", saved.LastError);
    }

    [Fact]
    public async Task Message_has_xml_and_dacte_attachments_and_subject()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"));
        Email("REM", "rem@x.com.br");
        await Db.SaveChangesAsync();
        var row = await EnqueueAsync(key);
        var doc = await Db.Context.TransportDocuments.AsNoTracking().SingleAsync();

        await Job().ExecuteAsync("0001", row.Key);

        var msg = Assert.Single(_sender.SentMessages).Message;
        Assert.Equal("CT-e 100/1 - TRANSPORTADORA TESTE LTDA", msg.Subject);
        Assert.Equal([$"CTe{doc.AccessKey}.xml", $"DACTE{doc.AccessKey}.pdf"], msg.Attachments.Select(a => a.FileName));
        Assert.Equal((NfeEmailDocumentType.Transport, key, "0001"), Assert.Single(_dacte.Calls));
    }

    [Fact]
    public async Task Cancellation_email_subject_and_event_xml()
    {
        var key = await SeedCteAsync(true, (CtePartyRole.Sender, "REM"));
        Db.Context.TransportDocumentXmls.Add(new TransportDocumentXml
            { TransportDocumentKey = key, Kind = CteXmlKind.Cancellation, Xml = CancelXml });
        Email("REM", "rem@x.com.br");
        await Db.SaveChangesAsync();
        var row = await EnqueueAsync(key, NfeEmailEventType.Cancelled);

        await Job().ExecuteAsync("0001", row.Key);

        var msg = Assert.Single(_sender.SentMessages).Message;
        Assert.Equal("Cancelamento do CT-e 100/1 - TRANSPORTADORA TESTE LTDA", msg.Subject);
        Assert.Equal(3, msg.Attachments.Count);
    }

    [Fact]
    public async Task Auto_send_disabled_enqueues_nothing()
    {
        var key = await SeedCteAsync(false, (CtePartyRole.Sender, "REM"));

        await Hooks().OnAuthorizedAsync(await Db.Context.TransportDocuments.SingleAsync(x => x.Key == key));

        Assert.Empty(await Db.Context.NfeEmailMessages.ToListAsync());
    }
}
