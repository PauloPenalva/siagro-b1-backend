using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsGetService(AppDbContext context, ILogger<WeighingTicketsGetService> logger)
{
    public async Task<WeighingTicket?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.WeighingTickets
                .Include(x => x.QualityInspections)
                .ThenInclude(x => x.QualityAttrib)
                .FirstOrDefaultAsync(x => x.Key == key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<WeighingTicket> QueryAll()
    {
        return context.WeighingTickets.AsNoTracking();
    }
}