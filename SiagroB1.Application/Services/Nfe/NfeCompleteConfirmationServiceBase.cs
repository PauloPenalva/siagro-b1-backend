using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir confirmação": refaz a confirmação de um documento com NF-e já autorizada.</summary>
public abstract class NfeCompleteConfirmationServiceBase<TDocument>(
    INfeDocumentStore<TDocument> store, NfeResultHandlerBase<TDocument> resultHandler)
    where TDocument : class, INfeDocument
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await store.FindReadOnlyAsync(key) ?? throw new NotFoundException(store.NotFoundMessage);

        if (invoice.NfeStatus != NfeStatus.Authorized || invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento com NF-e autorizada e ainda Pendente tem confirmação a concluir.");

        return await resultHandler.ConfirmAsync(key, userName);
    }
}
