using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.OwnershipTransfers;

public class OwnershipTransfersUpdateService(
    IUnitOfWork db,
    IItemService itemService,
    OwnershipTransfersValidateContractService validateContractService,
    IStringLocalizer<Resource> resource,
    ILogger<OwnershipTransfersUpdateService> logger)
{
    public async Task<OwnershipTransfer?> ExecuteAsync(Guid key, OwnershipTransfer ownershipTransfer, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        var existingEntity = await db.Context.OwnershipTransfers
                                 .FirstOrDefaultAsync(x => x.Key == key) ??
                             throw new NotFoundException(resource["OWNERSHIP_TRANSFER_NOT_FOUND"].Value);

        // Guarda de status única para PUT e PATCH: o controller do PATCH aplica o Delta
        // antes de chamar aqui, então o status precisa vir da cópia rastreada do banco.
        if (existingEntity.TransferStatus != OwnershipTransferStatus.Open)
            throw new ApplicationException(resource["OWNERSHIP_TRANSFER_CLOSED_UPDATE"].Value);

        try
        {
            // Atribuição explícita no lugar de CurrentValues.SetValues: o payload do OData
            // carrega a entidade inteira, e o SetValues deixava um PUT/PATCH reescrever
            // TransferStatus, TransferCode e as colunas de auditoria.
            existingEntity.Date = ownershipTransfer.Date;
            existingEntity.BranchCode = ownershipTransfer.BranchCode;
            existingEntity.DocNumberKey = ownershipTransfer.DocNumberKey;
            existingEntity.ItemCode = ownershipTransfer.ItemCode;
            existingEntity.UomCode = ownershipTransfer.UomCode;
            existingEntity.StorageAddressOriginCode = ownershipTransfer.StorageAddressOriginCode;
            existingEntity.StorageAddressDestinationCode = ownershipTransfer.StorageAddressDestinationCode;
            existingEntity.Quantity = ownershipTransfer.Quantity;
            existingEntity.Comments = ownershipTransfer.Comments;
            existingEntity.PurchaseContractKey = ownershipTransfer.PurchaseContractKey;

            existingEntity.UpdatedAt = DateTime.Now;
            existingEntity.UpdatedBy = userName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(ownershipTransfer.ItemCode))?.ItemName;

            // Revalida a cada edição: origem, destino, produto e quantidade podem ter
            // mudado nesta mesma gravação.
            existingEntity.PurchaseContractCode =
                (await validateContractService.ValidateForPersistAsync(existingEntity))?.Code;

            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
            logger.LogError(e, e.Message);
            throw new DefaultException($"Erro ao atualizar transferencia: {e.Message}");
        }

        return existingEntity;
    }
}
