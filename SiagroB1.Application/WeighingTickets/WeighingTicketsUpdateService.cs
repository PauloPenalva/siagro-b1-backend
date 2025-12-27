using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsUpdateService(IUnitOfWork db, ILogger<WeighingTicketsUpdateService> logger)
{
    public async Task<WeighingTicket?> ExecuteAsync(Guid key, WeighingTicket entity, string userName)
    {
        var existingEntity = await db.Context.WeighingTickets
                                 .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");

        if (existingEntity.Status == WeighingTicketStatus.Complete)
        {
            throw new ApplicationException("Weighing ticket is in complete status.");
        }
        
        try
        {
            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}