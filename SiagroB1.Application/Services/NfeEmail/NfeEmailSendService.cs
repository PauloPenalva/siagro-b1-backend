using Hangfire;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Jobs;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Services.NfeEmail;

/// <summary>
/// Reenvio manual do e-mail da NF-e (DANFE + XML) pela tela da nota. O varredor só pega linhas automáticas,
/// então o envio é enfileirado aqui mesmo, depois do save (a linha já está commitada para o worker ler).
/// </summary>
public class NfeEmailSendService(
    IUnitOfWork db,
    INfeEmailOutboxService outbox,
    INfeEmailRecipientResolver recipientResolver,
    IEmailSettingsService emailSettings,
    IBackgroundJobClient backgroundJobClient,
    ICompanyContext companyContext)
{
    public async Task<NfeEmailMessage> SendAsync(
        NfeEmailDocumentType type, Guid key, IReadOnlyList<string> recipients, string userName)
    {
        NfeEmailEventType evt;
        if (type == NfeEmailDocumentType.Transport)
        {
            var (_, cteStatus) = await LoadTransportAsync(key);
            evt = cteStatus switch
            {
                CteStatus.Authorized => NfeEmailEventType.Authorized,
                CteStatus.Cancelled => NfeEmailEventType.Cancelled,
                _ => throw new DefaultException("Só é possível enviar CT-e autorizado ou cancelado."),
            };
        }
        else
        {
            var (_, status) = await LoadAsync(type, key);
            evt = status switch
            {
                NfeStatus.Authorized => NfeEmailEventType.Authorized,
                NfeStatus.Cancelled => NfeEmailEventType.Cancelled,
                _ => throw new DefaultException("Só é possível enviar NF-e autorizada ou cancelada."),
            };
        }

        var emails = NfeEmailAddresses.Normalize(recipients);
        if (emails.Count == 0)
            throw new DefaultException("Informe ao menos um e-mail de destino.");

        if (await emailSettings.GetSmtpAsync() is null)
            throw new DefaultException("Configure o envio de e-mail da empresa antes de enviar o e-mail.");

        var row = await outbox.AddManualAsync(type, key, evt, emails, userName);

        row.EnqueuedAt = DateTime.Now;
        await db.SaveChangesAsync();

        backgroundJobClient.Enqueue<NfeEmailDispatchJob>(
            job => job.ExecuteAsync(companyContext.CompanyCode, row.Key, CancellationToken.None));

        return row;
    }

    /// <summary>E-mails de NF-e do parceiro da nota, para pré-preencher o diálogo de envio.</summary>
    public async Task<IReadOnlyList<string>> DefaultRecipientsAsync(NfeEmailDocumentType type, Guid key)
    {
        if (type == NfeEmailDocumentType.Transport)
            return await recipientResolver.ResolveManyAsync((await LoadTransportAsync(key)).CardCodes);

        var (cardCode, _) = await LoadAsync(type, key);
        return await recipientResolver.ResolveAsync(cardCode);
    }

    /// <summary>Tomador, remetente e destinatário do CT-e (mesma regra do envio automático).</summary>
    private async Task<(IReadOnlyList<string> CardCodes, CteStatus Status)> LoadTransportAsync(Guid key)
    {
        var doc = await db.Context.TransportDocuments.AsNoTracking().Include(x => x.Parties)
                      .FirstOrDefaultAsync(x => x.Key == key)
                  ?? throw new NotFoundException("CT-e não encontrado.");

        return (NfeEmailDispatchJob.TransportCardCodes(doc), doc.Status);
    }

    private async Task<(string CardCode, NfeStatus Status)> LoadAsync(NfeEmailDocumentType type, Guid key)
    {
        if (type == NfeEmailDocumentType.Purchase)
        {
            var purchase = await db.Context.PurchaseInvoices.AsNoTracking().Where(i => i.Key == key)
                               .Select(i => new { i.CardCode, i.NfeStatus, i.IssuerType }).FirstOrDefaultAsync()
                           ?? throw new NotFoundException("Documento de entrada não encontrado.");

            if (purchase.IssuerType != DocumentIssuerType.Own)
                throw new DefaultException("Esta nota não foi emitida pela empresa.");

            return (purchase.CardCode, purchase.NfeStatus);
        }

        var sales = await db.Context.SalesInvoices.AsNoTracking().Where(i => i.Key == key)
                        .Select(i => new { i.CardCode, i.NfeStatus }).FirstOrDefaultAsync()
                    ?? throw new NotFoundException("Documento de saída não encontrado.");

        return (sales.CardCode, sales.NfeStatus);
    }
}
