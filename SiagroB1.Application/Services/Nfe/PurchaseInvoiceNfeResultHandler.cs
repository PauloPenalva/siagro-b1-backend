using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Retorno da SEFAZ no documento de entrada; confirma pelo <see cref="PurchaseInvoicesConfirmService"/>.</summary>
public class PurchaseInvoiceNfeResultHandler(
    IUnitOfWork db, PurchaseInvoicesConfirmService confirm, ILogger<PurchaseInvoiceNfeResultHandler> logger)
    : NfeResultHandlerBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), logger)
{
    protected override Task ConfirmDocumentAsync(Guid key, string userName) => confirm.ExecuteAsync(key, userName);
}
