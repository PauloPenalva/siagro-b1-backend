using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCancelService(
    AppDbContext db,
    WeighingTicketsGetService weighingTicketsGetService,
    StorageTransactionsCancelService storageTransactionsCancelService
    )
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var ticket = await weighingTicketsGetService.GetByIdAsync(key) 
            ?? throw new KeyNotFoundException($"Weighing ticket not found");

        if (ticket.Status != WeighingTicketStatus.Complete)
        {
            throw new ApplicationException("Weighing receipts can only be cancelled if the status is \"complete\".");
        }

        var sa = await GetStorageTransactionByWeighingTicketKey(key);
        
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            if (sa != null && sa.Key != null)
            {
                await storageTransactionsCancelService.ExecuteAsync((Guid) sa.Key, userName, TransactionCode.WeighingTicket);
            }

            ticket.Status = WeighingTicketStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }

    private async Task<StorageTransaction?> GetStorageTransactionByWeighingTicketKey(Guid key)
    {
        return await db.StorageTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WeighingTicketKey == key);
    }
}