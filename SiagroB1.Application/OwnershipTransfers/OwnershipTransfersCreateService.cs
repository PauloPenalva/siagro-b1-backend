using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.OwnershipTransfers;

public class OwnershipTransfersCreateService(
    IUnitOfWork db,
    IItemService itemService,
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