using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeleteService(AppDbContext context, ILogger<SalesContractsDeleteService> logger)
{
    /// <summary>
    /// Exclui o contrato em rascunho junto com os filhos que um rascunho pode ter. As FKs para SALES_CONTRACTS são
    /// NoAction, e o contrato "Fixo" já nasce com uma fixação automática: sem remover os filhos antes, o SQL Server
    /// recusava a exclusão (500). Mesmo padrão de <c>PurchaseContractsDeleteService</c>.
    /// </summary>
    public async Task<bool> ExecuteAsync(Guid key)
    {
        return await DeleteAsyncWithTransaction(key, async entity =>
        {
            var minutas = await context.ContractDrafts
                .Where(x => x.SalesContractKey == entity.Key)
                .ToListAsync();

            // Minuta enviada (ou assinada) tem envelope na assinatura eletrônica: excluir o contrato o deixaria órfão.
            if (minutas.Any(x => x.Status is not (ContractDraftStatus.Draft or ContractDraftStatus.Canceled)))
                throw new ApplicationException(
                    "O contrato tem minuta enviada para assinatura: cancele a minuta antes de excluir o contrato.");

            var minutaKeys = minutas.Select(x => x.Key).ToList();
            context.ContractDraftSigners.RemoveRange(
                await context.ContractDraftSigners.Where(x => minutaKeys.Contains(x.DraftKey)).ToListAsync());
            context.ContractDrafts.RemoveRange(minutas);

            context.SalesContractsPriceFixations.RemoveRange(
                await context.SalesContractsPriceFixations.Where(x => x.SalesContractKey == entity.Key).ToListAsync());
            context.SalesContractsDeliveryLocations.RemoveRange(
                await context.SalesContractsDeliveryLocations.Where(x => x.SalesContractKey == entity.Key).ToListAsync());
            context.SalesContractsComments.RemoveRange(
                await context.SalesContractsComments.Where(x => x.SalesContractKey == entity.Key).ToListAsync());
            context.SalesContractsChangeLogs.RemoveRange(
                await context.SalesContractsChangeLogs.Where(x => x.SalesContractKey == entity.Key).ToListAsync());
            context.SalesContractAttachments.RemoveRange(
                await context.SalesContractAttachments.Where(x => x.SalesContractKey == entity.Key).ToListAsync());

            // A FK é cascade no banco, mas removido aqui também: o resultado não depende do provider.
            context.SalesContractFiscalComplements.RemoveRange(
                await context.SalesContractFiscalComplements.Where(x => x.SalesContractKey == entity.Key).ToListAsync());

            await context.SaveChangesAsync();
        });
    }
    
    private async Task<bool> DeleteAsyncWithTransaction(Guid key, Func<SalesContract, Task>? preDeleteAction = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var entity = await context.SalesContracts
                .FirstOrDefaultAsync(x => x.Key == key);
            
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", nameof(SalesContract), key);
                return false;
            }

            if (entity.Status != ContractStatus.Draft)
            {
                throw new ApplicationException("Sales contract must be in draft status.");
            }
            
            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.SalesContracts.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(SalesContract), key);
            throw;
        }
    }
}