using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Cancelamento registrado no documento de saída; estorna pelo <see cref="SalesInvoicesCancelService"/>.</summary>
public class SalesInvoiceNfeCancellationHandler(
    IUnitOfWork db, SalesInvoicesCancelService cancel, ILogger<SalesInvoiceNfeCancellationHandler> logger,
    INfeEmailOutboxService? emailOutbox = null)
    : NfeCancellationHandlerBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), logger, emailOutbox)
{
    protected override NfeEmailDocumentType EmailDocumentType => NfeEmailDocumentType.Sales;

    public override Task EnsureCanCancelAsync(Guid key) => cancel.EnsureCanCancelAsync(key);

    protected override Task CancelDocumentAsync(Guid key, string userName) => cancel.CancelAfterNfeAsync(key, userName);
}
