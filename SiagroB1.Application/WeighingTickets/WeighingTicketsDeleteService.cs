using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsDeleteService(AppDbContext context, ILogger<WeighingTicketsDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await context.WeighingTickets
            .FirstOrDefaultAsync(x => x.Key == key) ?? 
                     throw new NotFoundException("Weighing ticket not found.");

        if (entity.Status != WeighingTicketStatus.Waiting)
        {
            throw new ApplicationException("Weighing tickets are not waiting.");
        }
        
        await context.Entry(entity).Collection(e => e.QualityInspections).LoadAsync();
        
        if (entity.QualityInspections.Any())
            context.QualityInspections.RemoveRange(entity.QualityInspections);
        
        context.WeighingTickets.Remove(entity);
        await context.SaveChangesAsync();
        return true;
    }
}