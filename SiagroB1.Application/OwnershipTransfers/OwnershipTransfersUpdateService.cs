using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.OwnershipTransfers;

public class OwnershipTransfersUpdateService(
    IUnitOfWork db,
    IItemService itemService,
    ILogger<OwnershipTransfersUpdateService> logger)
{
    public async Task<OwnershipTransfer?> ExecuteAsync(Guid key, OwnershipTransfer ownershipTransfer, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var existingEntity = await db.Context.OwnershipTransfers
                                 .FirstOrDefaultAsync(x => x.Key == key) ??
                             throw new NotFoundException("Transferencia de propriedade não encontrada.");
        
        try
        {
            db.Context.Entry(existingEntity).CurrentValues.SetValues(ownershipTransfer);

            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(ownershipTransfer.ItemCode))?.ItemName;
            
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao atualizar transferencia: {e.Message}");
        }

        return ownershipTransfer;
    }
}