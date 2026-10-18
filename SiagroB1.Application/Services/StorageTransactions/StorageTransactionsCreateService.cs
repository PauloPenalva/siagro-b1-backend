using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.Royalties;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsCreateService(
    IUnitOfWork unitOfWork,
    DocNumberSequenceService numberSequenceService,
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentReleaseMovementGuardService movementGuard,
    ILogger<StorageTransactionsCreateService> logger)
{
    public async Task<StorageTransaction> ExecuteAsyncWithTransaction(
            StorageTransaction entity, 
            string userName, 
            TransactionCode transactionCode = TransactionCode.StorageTransaction
        )
    {
        try
        {
            await unitOfWork.BeginTransactionAsync();
            
            await ExecuteAsync(entity, userName, transactionCode);
            
            await unitOfWork.CommitAsync();
            
            return entity;
        }
        catch (ApplicationException)
        {
            // Recusa de negócio (ex.: série de numeração inválida): a mensagem pt-BR segue para o usuário.
            await unitOfWork.RollbackAsync();
            throw;
        }
        catch (Exception ex)
        {
            await unitOfWork.RollbackAsync();
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
    
    public async Task<StorageTransaction> ExecuteAsync(
        StorageTransaction entity, 
        string userName, 
        TransactionCode transactionCode = TransactionCode.StorageTransaction,
        CommitMode commitMode = CommitMode.Auto)
    {
        // Antes do try (cujo catch embrulha em DefaultException): bloqueia romaneio
        // contra liberação finalizada/cancelada/pausada.
        await movementGuard.EnsureCanShipAsync(entity);

        // Também antes do try: a recusa de série (pt-BR) não pode ser embrulhada em "Error creating entity".
        // Romaneio não tem série escolhida pelo usuário: a chave herdada (cópia, troca de liberação, entrada)
        // só vale se ainda for automática e ativa; senão vale a padrão. O Code do corpo é ignorado (TypedCode null).
        var assignment = await numberSequenceService.AssignAsync(new DocNumberRequest(
            TransactionCode.StorageTransaction, await numberSequenceService.ResolveInheritedKeyAsync(entity.DocNumberKey),
            null, 50, c => unitOfWork.Context.StorageTransactions.AnyAsync(x => x.Code == c)));
        entity.DocNumberKey = assignment.DocNumberKey;
        entity.Code = assignment.Code;

        try
        {
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            entity.RoyaltyTestCode = RoyaltyRules.NormalizeTestCode(entity.RoyaltySituation, entity.RoyaltyTestCode);
            entity.RoyaltyOriginName = await RoyaltyRules.ResolveOriginNameAsync(
                entity, async code => (await businessPartnerService.GetByIdAsync(code))?.CardName);
            entity.TransactionOrigin = transactionCode;
            entity.RecalculateFreightPrice();
            entity.CreatedBy = userName;
            entity.CreatedAt = DateTime.Now;
            
            await unitOfWork.Context.StorageTransactions.AddAsync(entity);
            
            if (commitMode == CommitMode.Auto)
            {
                await unitOfWork.SaveChangesAsync();

                if (entity.ShipmentReleaseKey.HasValue &&
                    ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(entity.TransactionType))
                {
                    await recalcShipped.RecalculateAsync(entity.ShipmentReleaseKey.Value);
                }
            }

            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }
}