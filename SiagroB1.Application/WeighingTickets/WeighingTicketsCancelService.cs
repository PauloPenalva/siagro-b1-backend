using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCancelService(
    IUnitOfWork db,
    WeighingTicketsGetService weighingTicketsGetService,
    StorageTransactionsCancelService storageTransactionsCancelService,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var ticket = await weighingTicketsGetService.GetByIdAsync(key) 
            ?? throw new KeyNotFoundException($"Weighing ticket not found");

        if (ticket.Status != WeighingTicketStatus.Complete)
        {
            throw new ApplicationException(resource["WEIGHING_TICKET_CANCEL_SERVICE_NOT_COMPLETE_MSG"]);
        }

        var sa = await GetStorageTransactionByWeighingTicketKey(key);
        
        try
        {
            await db.BeginTransactionAsync();
            
            if (sa != null)
            {
                await storageTransactionsCancelService.ExecuteAsync((Guid) sa.Key, userName, TransactionCode.WeighingTicket);
            }

            ticket.Status = WeighingTicketStatus.Cancelled;
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    private async Task<StorageTransaction?> GetStorageTransactionByWeighingTicketKey(Guid key)
    {
        return await db.Context.StorageTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WeighingTicketKey == key);
    }
}