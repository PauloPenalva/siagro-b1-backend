using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Domain.Dtos.Email;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Jobs;

/// <summary>
/// Envia UMA linha da fila de e-mail da NF-e (DANFE + XML). Fila própria com um worker, como o WhatsApp.
/// O primeiro argumento é a empresa (lido pelo CompanyJobFilter); o outro é só a Key da linha — o painel do
/// Hangfire mostra os argumentos, então nada de e-mail ou conteúdo neles.
/// Falha de SMTP ou do DANFE (Reports fora do ar) é tentativa falha: grava o erro e lança para o Hangfire
/// retentar; na 3ª tentativa a linha vira Failed. Nunca marca Sent sem o envio ter dado certo.
/// </summary>
[Queue(QueueName)]
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 120, 600])]
public class NfeEmailDispatchJob(
    IUnitOfWork db,
    INfeEmailRecipientResolver recipientResolver,
    IEmailSettingsService emailSettings,
    IEmailSender sender,
    IDanfeProvider danfeProvider,
    ILogger<NfeEmailDispatchJob> logger)
{
    public const string QueueName = "email";

    public const int MaxAttempts = 3;

    private const int MaxErrorLength = 1000;

    public async Task ExecuteAsync(string companyCode, Guid messageKey, CancellationToken ct = default)
    {
        var message = await db.Context.NfeEmailMessages.FirstOrDefaultAsync(m => m.Key == messageKey, ct);

        // Idempotência: o varredor pode reenfileirar a mesma linha; só Pending é trabalho.
        if (message is null || message.Status != NfeEmailStatus.Pending)
            return;

        if (message.DocumentType == NfeEmailDocumentType.Transport)
        {
            await ExecuteTransportAsync(message, companyCode, ct);
            return;
        }

        var document = message.DocumentType == NfeEmailDocumentType.Purchase
            ? (INfeDocument?)await new PurchaseInvoiceNfeStore(db).FindReadOnlyAsync(message.DocumentKey)
            : await new SalesInvoiceNfeStore(db).FindReadOnlyAsync(message.DocumentKey);

        if (document is null)
        {
            await ResolveAsync(message, NfeEmailStatus.Skipped, "Documento não encontrado.", ct);
            return;
        }

        var recipients = message.Trigger == NfeEmailTrigger.Manual
            ? Split(message.Recipients)
            : await recipientResolver.ResolveAsync(document.CardCode);

        if (recipients.Count == 0)
        {
            await ResolveAsync(message, NfeEmailStatus.Skipped, "Parceiro sem e-mail de NF-e", ct);
            return;
        }

        SmtpSettings? smtp;
        try
        {
            smtp = await emailSettings.GetSmtpAsync();
        }
        catch (DefaultException e)
        {
            // Ex.: Nfe:CertificateKey ausente — não se resolve retentando.
            await ResolveAsync(message, NfeEmailStatus.Failed, Truncate(e.Message), ct);
            return;
        }

        if (smtp is null)
        {
            await ResolveAsync(message, NfeEmailStatus.Failed, "Configure o envio de e-mail da empresa", ct);
            return;
        }

        string procXml;
        string? eventXml = null;
        byte[] danfe;

        try
        {
            var xmls = Source(message);
            procXml = await xmls.LatestAuthorizedXmlAsync(message.DocumentKey);
            if (message.EventType == NfeEmailEventType.Cancelled)
                eventXml = await xmls.LatestXmlAsync(message.DocumentKey, NfeXmlKind.CancellationEvent);
        }
        catch (NotFoundException e)
        {
            await ResolveAsync(message, NfeEmailStatus.Failed, Truncate(e.Message), ct);
            return;
        }

        try
        {
            danfe = await danfeProvider.GetPdfAsync(message.DocumentType, message.DocumentKey, companyCode, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await FailAttemptAsync(message, "Falha ao gerar o DANFE: " + e.Message, ct);
            return;
        }

        var (subject, body, attachments) = NfeEmailMessageBuilder.Build(message.EventType, procXml, eventXml, danfe);
        var result = await sender.SendAsync(new EmailMessage(recipients, subject, body, attachments), smtp, ct);

        if (!result.Success)
        {
            await FailAttemptAsync(message, "Falha no envio: " + result.Error, ct);
            return;
        }

        message.AttemptCount++;
        message.Status = NfeEmailStatus.Sent;
        message.SentAt = DateTime.Now;
        message.LastAttemptAt = message.SentAt;
        message.LastError = null;
        message.Recipients = string.Join(";", recipients);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// CT-e: mesmo ciclo da NF-e (Skipped sem destinatário, Failed sem SMTP, retentativa do Hangfire), com cteProc + DACTE
    /// e os e-mails de NF-e do tomador, remetente e destinatário.
    /// </summary>
    private async Task ExecuteTransportAsync(NfeEmailMessage message, string companyCode, CancellationToken ct)
    {
        var doc = await db.Context.TransportDocuments.AsNoTracking().Include(x => x.Parties)
            .FirstOrDefaultAsync(x => x.Key == message.DocumentKey, ct);

        if (doc is null)
        {
            await ResolveAsync(message, NfeEmailStatus.Skipped, "Documento não encontrado.", ct);
            return;
        }

        IReadOnlyList<string> recipients;
        if (message.Trigger == NfeEmailTrigger.Manual)
            recipients = Split(message.Recipients);
        else
        {
            var taker = doc.Parties.FirstOrDefault(p => (int)p.Role == doc.TakerRole)?.CardCode;
            var codes = new List<string?> { taker };
            codes.AddRange(new[] { CtePartyRole.Sender, CtePartyRole.Recipient }
                .Select(role => doc.Parties.FirstOrDefault(p => p.Role == role)?.CardCode));
            recipients = await recipientResolver.ResolveManyAsync(codes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!).ToList());
        }

        if (recipients.Count == 0)
        {
            await ResolveAsync(message, NfeEmailStatus.Skipped, "Parceiro sem e-mail de NF-e", ct);
            return;
        }

        SmtpSettings? smtp;
        try
        {
            smtp = await emailSettings.GetSmtpAsync();
        }
        catch (DefaultException e)
        {
            await ResolveAsync(message, NfeEmailStatus.Failed, Truncate(e.Message), ct);
            return;
        }

        if (smtp is null)
        {
            await ResolveAsync(message, NfeEmailStatus.Failed, "Configure o envio de e-mail da empresa", ct);
            return;
        }

        var procXml = await db.Context.TransportDocumentXmls.AsNoTracking()
            .Where(x => x.TransportDocumentKey == doc.Key && x.Kind == CteXmlKind.Authorized)
            .Select(x => x.Xml).FirstOrDefaultAsync(ct);

        if (procXml is null)
        {
            await ResolveAsync(message, NfeEmailStatus.Failed, "XML autorizado do CT-e não encontrado.", ct);
            return;
        }

        var eventXml = message.EventType == NfeEmailEventType.Cancelled
            ? await db.Context.TransportDocumentXmls.AsNoTracking()
                .Where(x => x.TransportDocumentKey == doc.Key && x.Kind == CteXmlKind.Cancellation)
                .Select(x => x.Xml).FirstOrDefaultAsync(ct)
            : null;

        byte[] dacte;
        try
        {
            dacte = await danfeProvider.GetPdfAsync(NfeEmailDocumentType.Transport, doc.Key, companyCode, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await FailAttemptAsync(message, "Falha ao gerar o DACTE: " + e.Message, ct);
            return;
        }

        var (subject, body, attachments) = NfeEmailMessageBuilder.BuildCte(
            message.EventType, doc.Number, doc.Series, doc.AccessKey ?? string.Empty, procXml, eventXml, dacte);
        var result = await sender.SendAsync(new EmailMessage(recipients, subject, body, attachments), smtp, ct);

        if (!result.Success)
        {
            await FailAttemptAsync(message, "Falha no envio: " + result.Error, ct);
            return;
        }

        message.AttemptCount++;
        message.Status = NfeEmailStatus.Sent;
        message.SentAt = DateTime.Now;
        message.LastAttemptAt = message.SentAt;
        message.LastError = null;
        message.Recipients = string.Join(";", recipients);
        await db.SaveChangesAsync();
    }

    private INfeXmlSource Source(NfeEmailMessage message) => message.DocumentType == NfeEmailDocumentType.Purchase
        ? new XmlSource<PurchaseInvoice>(new PurchaseInvoiceNfeStore(db))
        : new XmlSource<SalesInvoice>(new SalesInvoiceNfeStore(db));

    private async Task FailAttemptAsync(NfeEmailMessage message, string error, CancellationToken ct)
    {
        message.AttemptCount++;
        message.LastAttemptAt = DateTime.Now;
        message.LastError = Truncate(error);

        var retry = message.AttemptCount < MaxAttempts;
        if (!retry)
            message.Status = NfeEmailStatus.Failed;

        await db.SaveChangesAsync();

        if (retry)
        {
            logger.LogWarning("Falha no e-mail da NF-e {MessageKey} (tentativa {Attempt}): {Error}",
                message.Key, message.AttemptCount, message.LastError);

            // Lançar é o pedido de retentativa ao Hangfire.
            throw new ApplicationException($"Falha no e-mail da NF-e {message.Key}: {message.LastError}");
        }
    }

    private async Task ResolveAsync(NfeEmailMessage message, NfeEmailStatus status, string error, CancellationToken ct)
    {
        message.Status = status;
        message.LastError = error;
        message.LastAttemptAt = DateTime.Now;
        await db.SaveChangesAsync();
    }

    private static IReadOnlyList<string> Split(string? recipients) =>
        (recipients ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Truncate(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

    private interface INfeXmlSource
    {
        Task<string> LatestAuthorizedXmlAsync(Guid key);
        Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind);
    }

    private sealed class XmlSource<T>(INfeDocumentStore<T> store) : INfeXmlSource where T : class, INfeDocument
    {
        public Task<string> LatestAuthorizedXmlAsync(Guid key) => store.LatestAuthorizedXmlAsync(key);
        public Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind) => store.LatestXmlAsync(key, kind);
    }
}
