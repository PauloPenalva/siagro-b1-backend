using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsPriceFixationsGetService(
    AppDbContext context,
    ILogger<SalesContractsPriceFixationsGetService> logger)
{
    public async Task<SalesContractPriceFixation?> GetByIdAsync(Guid associationKey)
    {
        try
        {
            return await context.SalesContractsPriceFixations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", associationKey);
            throw new DefaultException("Error fetching entity");
        }
    }

    public async Task<SalesContractPriceFixation?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!ExistSalesContract(key))
            {
                throw new NotFoundException("Sales contract key not found");
            }

            return await context.SalesContractsPriceFixations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<SalesContractPriceFixation> QueryAll(Guid parentKey)
    {
        return context.SalesContractsPriceFixations
            .Where(x => x.SalesContractKey == parentKey)
            .AsNoTracking();
    }

    /// <summary>
    /// Fila da diretoria: fixações em aprovação de contratos a fixar (PAF), de todos os
    /// contratos. Inclui o contrato para a UI mostrar código, cliente e produto sem nova query.
    /// </summary>
    /// <remarks>
    /// Filtra por <see cref="ContractType.ToBeDetermined"/> de propósito: contrato de preço
    /// fixo tem uma fixação automática que espelha o preço já acordado na negociação, e ela
    /// não é um pedido de aprovação.
    /// </remarks>
    public IQueryable<SalesContractPriceFixation> QueryPending()
    {
        return context.SalesContractsPriceFixations
            .Include(x => x.SalesContract)
            .Where(x => x.Status == PriceFixationStatus.InApproval
                        && x.SalesContract!.Type == ContractType.ToBeDetermined)
            .AsNoTracking();
    }

    private bool ExistSalesContract(Guid key)
    {
        return context.SalesContracts.Any(x => x.Key == key);
    }
}
