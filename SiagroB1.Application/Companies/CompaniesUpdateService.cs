using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Companies;

public class CompaniesUpdateService(CommonDbContext db, ILogger<CompaniesUpdateService> logger)
{
    public async Task<Company?> ExecuteAsync(string code, Company entity)
    {
        var existingEntity = await db.Companies
            .FirstOrDefaultAsync(tc => tc.Code == code) ?? 
                             throw new NotFoundException("Company not found.");
        try
        {
            db.Entry(existingEntity).CurrentValues.SetValues(entity);

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