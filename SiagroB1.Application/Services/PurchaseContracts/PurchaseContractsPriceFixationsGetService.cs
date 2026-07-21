using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsPriceFixationsGetService(AppDbContext context, ILogger<PurchaseContractsPriceFixationsGetService> logger)
{
    public async Task<PurchaseContractPriceFixation?> GetByIdAsync(Guid associationKey)
    {
        try
        {
            return await context.PurchaseContractsPriceFixations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", associationKey);
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<PurchaseContractPriceFixation?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!ExistPurchaseContract(key))
            {
                throw new NotFoundException("Purchase contract key not found");
            }
            
            return await context.PurchaseContractsPriceFixations.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<PurchaseContractPriceFixation> QueryAll(Guid parentKey)
    {
        return context.PurchaseContractsPriceFixations
            .Where(x => x.PurchaseContractKey == parentKey)
            .AsNoTracking();
    }

    /// <summary>
    /// Fila da diretoria: fixações em aprovação de contratos a fixar (PAF), de todos os
    /// contratos. Inclui o contrato para a UI mostrar código, fornecedor e produto sem
    /// nova query.
    /// </summary>
    /// <remarks>
    /// Filtra por <see cref="ContractType.ToBeDetermined"/> de propósito: contrato de preço
    /// fixo tem uma fixação automática que espelha o preço já acordado na negociação, e ela
    /// não é um pedido de aprovação. Sem este filtro a fila nasce com centenas de itens que
    /// ninguém deve aprovar.
    /// </remarks>
    public IQueryable<PurchaseContractPriceFixation> QueryPending()
    {
        return context.PurchaseContractsPriceFixations
            .Include(x => x.PurchaseContract)
            .Where(x => x.Status == PriceFixationStatus.InApproval
                        && x.PurchaseContract!.Type == ContractType.ToBeDetermined)
            .AsNoTracking();
    }

    private bool ExistPurchaseContract(Guid key)
    {
        return context.PurchaseContracts.Any(x => x.Key == key);
    }
}