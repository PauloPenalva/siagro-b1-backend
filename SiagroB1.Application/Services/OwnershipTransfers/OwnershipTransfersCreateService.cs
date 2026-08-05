using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.OwnershipTransfers;

public class OwnershipTransfersCreateService(
    IUnitOfWork db,
    IItemService itemService,
    OwnershipTransfersValidateContractService validateContractService,
    DocNumberSequenceService numberSequenceService,
    ILogger<OwnershipTransfersCreateService> logger)
{
    public async Task ExecuteAsync(OwnershipTransfer ownershipTransfer, string userName, CommitMode commitMode = CommitMode.Auto)
    {
        ownershipTransfer.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.OwnershipTransfer);
        
        try
        {
            ownershipTransfer.CreatedAt = DateTime.Now;
            ownershipTransfer.CreatedBy = userName;
            ownershipTransfer.TransferCode = await numberSequenceService.GetDocNumber((Guid) ownershipTransfer.DocNumberKey);
            ownershipTransfer.TransferStatus = OwnershipTransferStatus.Open;
            ownershipTransfer.ItemName = (await itemService.GetByIdAsync(ownershipTransfer.ItemCode))?.ItemName;

            // Valida o vínculo já na gravação, para o usuário errar cedo. A confirmação
            // revalida — o saldo do contrato se move entre os dois momentos.
            ownershipTransfer.PurchaseContractCode =
                (await validateContractService.ValidateForPersistAsync(ownershipTransfer))?.Code;

            await db.Context.OwnershipTransfers.AddAsync(ownershipTransfer);
            
            if (commitMode == CommitMode.Auto)
                await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}