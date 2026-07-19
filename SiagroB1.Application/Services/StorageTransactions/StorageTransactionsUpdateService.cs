using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.StorageTransactions;

public class StorageTransactionsUpdateService(
    AppDbContext context,
    IUnitOfWork unitOfWork,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ILogger<StorageTransactionsUpdateService> logger)
{
    public async Task<StorageTransaction?> ExecuteAsync(
        Guid key, 
        StorageTransaction entity, 
        string userName, 
        CommitMode commitMode = CommitMode.Auto)
    {
        var existingEntity = await unitOfWork.Context.StorageTransactions
                                 .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");

        if (existingEntity.TransactionStatus != StorageTransactionsStatus.Pending)
        {
            throw new ApplicationException("Storage transaction must be in pending status.");
        }
        
        try
        {
            context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Depois do SetValues e em `existingEntity`: as colunas desnormalizadas
            // precisam acompanhar a troca do código, senão a tela mostra o nome antigo
            // ao lado do código novo na próxima leitura.
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            existingEntity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;

            existingEntity.UpdatedBy = userName;
            existingEntity.UpdatedAt = DateTime.Now;

            if (commitMode == CommitMode.Auto)
                await unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}