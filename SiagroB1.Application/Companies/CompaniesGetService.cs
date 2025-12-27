using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Companies;

public class CompaniesGetService(CommonDbContext db, ILogger<CompaniesUpdateService> logger)
{
    public async Task<Company?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);
            return await db.Companies
                .FirstOrDefaultAsync(p => p.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public IQueryable<Company> QueryAll()
    {
        return db.Companies.AsNoTracking();
    }
}