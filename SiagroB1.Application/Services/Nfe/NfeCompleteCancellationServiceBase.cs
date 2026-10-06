using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir cancelamento": refaz o cancelamento do documento cuja NF-e já foi cancelada na SEFAZ.</summary>
public abstract class NfeCompleteCancellationServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store, NfeCancellationHandlerBase<TDocument> handler, NfeNumberReservationService reservation)
    where TDocument : class, INfeDocument
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        await using var emissionLock = await reservation.AcquireEmissionLockAsync(key);

        var invoice = await store.FindReadOnlyAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        if (invoice.NfeStatus != NfeStatus.Cancelled || invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Só documento com NF-e cancelada e ainda ativo tem cancelamento a concluir.");

        return await handler.CompleteLocalAsync(key, userName);
    }
}
