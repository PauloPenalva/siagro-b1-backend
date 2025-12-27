using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Companies;

public class CompaniesDeleteService(CommonDbContext db, ILogger<CompaniesDeleteService> logger)
{
    public async Task ExecuteAsync(string code)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new NotFoundException("Company not found.");
        
        db.Companies.Remove(company);
        await db.SaveChangesAsync();
    }
}