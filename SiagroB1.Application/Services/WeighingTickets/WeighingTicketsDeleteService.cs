using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsDeleteService(
    IUnitOfWork db,
    ILogger<WeighingTicketsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await db.Context.WeighingTickets
            .FirstOrDefaultAsync(x => x.Key == key) ?? 
                     throw new NotFoundException("Weighing ticket not found.");

        if (entity.Status == WeighingTicketStatus.Complete)
        {
            throw new ApplicationException("Weighing tickets are not waiting.");
        }
        
        await db.Context.Entry(entity).Collection(e => e.QualityInspections).LoadAsync();
        
        if (entity.QualityInspections.Any())
            db.Context.QualityInspections.RemoveRange(entity.QualityInspections);
        
        db.Context.WeighingTickets.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
}