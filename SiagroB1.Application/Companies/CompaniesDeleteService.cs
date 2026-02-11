using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

using SiagroB1.Commons.Resources;
using Microsoft.Extensions.Localization;

namespace SiagroB1.Application.Companies;

public class CompaniesDeleteService(
    CommonDbContext db, 
    IStringLocalizer<Resource> resource,
    ILogger<CompaniesDeleteService> logger)
{
    public async Task ExecuteAsync(string code)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new NotFoundException(resource["COMPANY_NOT_FOUND"]);
        
        db.Companies.Remove(company);
        await db.SaveChangesAsync();
    }
}