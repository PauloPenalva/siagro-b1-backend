using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Retorno da SEFAZ no documento de saída; confirma pelo <see cref="SalesInvoicesConfirmService"/>.</summary>
public class SalesInvoiceNfeResultHandler(
    IUnitOfWork db, SalesInvoicesConfirmService confirm, ILogger<SalesInvoiceNfeResultHandler> logger)
    : NfeResultHandlerBase<SalesInvoice>(db, new SalesInvoiceNfeStore(db), logger)
{
    protected override Task ConfirmDocumentAsync(Guid key, string userName) => confirm.ExecuteAsync(key, userName);
}
