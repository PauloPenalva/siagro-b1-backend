using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cancelamento registrado no documento de entrada; estorna pelo <see cref="PurchaseInvoicesCancelService"/>.</summary>
public class PurchaseInvoiceNfeCancellationHandler(
    IUnitOfWork db, PurchaseInvoicesCancelService cancel, ILogger<PurchaseInvoiceNfeCancellationHandler> logger)
    : NfeCancellationHandlerBase<PurchaseInvoice>(db, new PurchaseInvoiceNfeStore(db), logger)
{
    public override Task EnsureCanCancelAsync(Guid key) => cancel.EnsureCanCancelAsync(key);

    protected override Task CancelDocumentAsync(Guid key, string userName) => cancel.CancelAfterNfeAsync(key, userName);
}
