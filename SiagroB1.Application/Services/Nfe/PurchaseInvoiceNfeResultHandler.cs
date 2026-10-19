using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Retorno da SEFAZ no documento de entrada; confirma pelo <see cref="PurchaseInvoicesConfirmService"/>.</summary>
public class PurchaseInvoiceNfeResultHandler(
    IUnitOfWork db, PurchaseInvoicesConfirmService confirm, ILogger<PurchaseInvoiceNfeResultHandler> logger,
    INfeEmailOutboxService? emailOutbox = null)
    : NfeResultHandlerBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), logger, emailOutbox)
{
    protected override NfeEmailDocumentType EmailDocumentType => NfeEmailDocumentType.Purchase;

    /// <summary>Só a entrada PRÓPRIA tem NF-e emitida aqui — a de terceiro não vai por e-mail.</summary>
    protected override bool SendsEmail(PurchaseInvoice document) => document.IssuerType == DocumentIssuerType.Own;

    protected override Task ConfirmDocumentAsync(Guid key, string userName) => confirm.ExecuteAsync(key, userName);
}
