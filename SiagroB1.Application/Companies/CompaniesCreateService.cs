using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Companies;

public class CompaniesCreateService(CommonDbContext db, ILogger<CompaniesCreateService> logger)
{
    public async Task<Company> ExecuteAsync(Company entity)
    {
        try
        {
            await db.Companies.AddAsync(entity);
            await db.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw new ApplicationException(ex.Message);
        }
    }    
}