using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsQualityInspectionsGetService(AppDbContext context, ILogger<WeighingTicketsQualityInspectionsGetService> logger)
{
    
    public async Task<QualityInspection?> GetByIdAsync(Guid key)
    {
        try
        {
            return await context.QualityInspections.FindAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<QualityInspection?> GetByIdAsync(Guid key, Guid associationKey)
    {
        try
        {
            if (!ExistQualityInspection(associationKey))
            {
                throw new NotFoundException("Quality inspection key not found.");
            }
            
            return await context.QualityInspections.FindAsync(associationKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", key);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<QualityInspection> QueryAll(Guid parentKey)
    {
        return context.QualityInspections
            .Where(x => x.WeighingTicketKey == parentKey)
            .AsNoTracking();
    }

    private bool ExistQualityInspection(Guid key)
    {
        return context.QualityInspections.Any(x => x.Key == key);
    }
}