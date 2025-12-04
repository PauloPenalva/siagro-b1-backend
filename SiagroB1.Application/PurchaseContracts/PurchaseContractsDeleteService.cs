using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Constants;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsDeleteService(AppDbContext context, ILogger<PurchaseContractsDeleteService> logger)
{
    /// <summary>
    /// Deleta o contrato de compra, somente se estiver com status de rascunho (Draft)
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            // Carrega filhos explicitamente
            await context.Entry(entity).Collection(e => e.PriceFixations).LoadAsync();
            await context.Entry(entity).Collection(e => e.QualityParameters).LoadAsync();
            await context.Entry(entity).Collection(e => e.Taxes).LoadAsync();
            await context.Entry(entity).Collection(e => e.ShipmentReleases).LoadAsync();
            await context.Entry(entity).Collection(e => e.Brokers).LoadAsync();
            
            // Remove filhos primeiro (caso o cascade não esteja funcionando)
            if (entity.PriceFixations.Any())
                context.PurchaseContractsPriceFixations.RemoveRange(entity.PriceFixations);

            if (entity.QualityParameters.Any())
                context.PurchaseContractsQualityParameters.RemoveRange(entity.QualityParameters);

            if (entity.Taxes.Any())
                context.PurchaseContractsTaxes.RemoveRange(entity.Taxes);
            
            if (entity.Brokers.Any())
                context.PurchaseContractsBrokers.RemoveRange(entity.Brokers);

            await context.SaveChangesAsync();
        });
    }
    
    /// <summary>
    /// Cria a transação para deleção dos registros referentes ao contrato.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="preDeleteAction"></param>
    /// <returns></returns>
    /// <exception cref="ApplicationException"></exception>
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<PurchaseContract, Task>? preDeleteAction = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var entity = await context.PurchaseContracts
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(PurchaseContract), key);
                return false;
            }

            if (entity.Status != ContractStatus.Draft)
            {
                throw new ApplicationException(MessagesPtBr.DeletePurchaseContractError);
            }
            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.PurchaseContracts.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(PurchaseContract), key);
            throw;
        }
    }
}