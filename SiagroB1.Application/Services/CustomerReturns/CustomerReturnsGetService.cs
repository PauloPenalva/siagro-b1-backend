using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Leitura das devoluções do cliente.
///
/// O Include de <c>Items.SalesInvoiceItem</c> NÃO é decorativo: a quebra apurada e a diferença
/// da linha são calculadas a partir da linha de origem, e sem ele voltam zero em silêncio —
/// toda linha pareceria divergente.
/// </summary>
public class CustomerReturnsGetService(IUnitOfWork db)
{
    public IQueryable<CustomerReturn> QueryAll()
    {
        return db.Context.CustomerReturns
            .Include(x => x.Items)
            .ThenInclude(i => i.SalesInvoiceItem)
            .AsNoTracking();
    }

    public async Task<CustomerReturn> GetByIdAsync(Guid key)
    {
        return await db.Context.CustomerReturns
                   .Include(x => x.Items)
                   .ThenInclude(i => i.SalesInvoiceItem)
                   .FirstOrDefaultAsync(x => x.Key == key)
               ?? throw new NotFoundException("Devolução de cliente não encontrada.");
    }
}
