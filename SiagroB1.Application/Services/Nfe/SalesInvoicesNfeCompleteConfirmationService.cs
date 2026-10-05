using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>"Concluir confirmação": refaz a confirmação de um documento com NF-e já autorizada.</summary>
public class SalesInvoicesNfeCompleteConfirmationService(IUnitOfWork db, SalesInvoiceNfeResultHandler resultHandler)
{
    public async Task<NfeIssueOutcomeDto> ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        if (invoice.NfeStatus != NfeStatus.Authorized || invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException("Só documento com NF-e autorizada e ainda Pendente tem confirmação a concluir.");

        return await resultHandler.ConfirmAsync(key, userName);
    }
}
