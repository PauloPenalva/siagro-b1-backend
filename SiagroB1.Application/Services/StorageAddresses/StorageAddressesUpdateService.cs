using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Infra.Queries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageAddresses;

public class StorageAddressesUpdateService(
    AppDbContext context, 
    IBusinessPartnerService  businessPartnerService,
    IItemService itemService,
    IWarehouseService warehouseService,
    ILogger<StorageAddressesUpdateService> logger)
{
    public async Task<StorageAddress?> ExecuteAsync(string code, StorageAddress entity, string userName)
    {
        var existingAddress = await context.StorageAddresses
                                  .FirstOrDefaultAsync(x => x.Code == code) ??
                              throw new NotFoundException("Storage address not found.");

        if (existingAddress.TransactionOrigin != TransactionCode.StorageAddress)
        {
            throw new ApplicationException("This record was created by another transaction. It cannot be updated.");
        }

        // Natureza é escolhida na criação e imutável (GAC-1181 fase 2). ⚠️ No fluxo OData (PATCH),
        // `existingAddress` e `entity` chegam sendo a MESMA instância rastreada, já com o valor
        // novo aplicado por Delta.Patch — comparar existingAddress.Nature com entity.Nature nunca
        // acusaria a mudança. Só OriginalValues ainda guarda o valor persistido no banco.
        var originalNature = context.Entry(existingAddress)
            .OriginalValues.GetValue<StorageAddressNature>(nameof(StorageAddress.Nature));

        if (entity.Nature != originalNature)
        {
            throw new ApplicationException(
                $"A natureza do lote {code} não pode ser alterada após a criação.");
        }

        // Beneficiamento fiscal (spec 2026-10-12 §3.2): trocar com nota de custódia ativa mudaria as etapas esperadas
        // de romaneios que já têm nota. OriginalValues pelo mesmo motivo da natureza (PATCH chega mutado).
        var originalProcessing = context.Entry(existingAddress).OriginalValues.GetValue<bool>(nameof(StorageAddress.RequiresProcessing));
        if (entity.RequiresProcessing != originalProcessing)
        {
            var lotTransactionKeys = await context.StorageTransactions.AsNoTracking()
                .Where(t => t.StorageAddressCode == code).Select(t => t.Key).ToListAsync();

            if ((await StorageCustodyQuery.ActiveDocumentsAsync(context, lotTransactionKeys)).Count > 0)
                throw new ApplicationException("O lote já possui notas fiscais de custódia; não é possível alterar o beneficiamento.");
        }

        try
        {
            entity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            entity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;
            entity.WarehouseName = (await warehouseService.GetByIdAsync(entity.WarehouseCode))?.Name;
            context.Entry(existingAddress).CurrentValues.SetValues(entity);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}