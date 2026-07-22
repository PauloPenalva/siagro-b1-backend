using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsAllocationGetService(
    AppDbContext context,
    ILogger<SalesContractsAllocationGetService> logger)
{
    public async Task<SalesContractAllocation?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.SalesContractsAllocations
                .Include(x => x.SalesContract)
                .Include(x => x.SalesInvoiceItem)!.ThenInclude(i => i!.SalesInvoice)
                .Include(x => x.SalesShipmentRelease)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Erro ao buscar alocação de contrato de venda {Key}.", key);
            throw;
        }
    }

    /// <summary>
    /// Alimenta o entity set OData (tela de alocações de venda). As navegações ficam a
    /// cargo do $expand do cliente.
    /// </summary>
    public IQueryable<SalesContractAllocation> QueryAll()
    {
        return context.SalesContractsAllocations.AsNoTracking();
    }
}
