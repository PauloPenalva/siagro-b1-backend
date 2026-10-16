using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

public class CompaniesDeleteService(
    CommonDbContext db,
    ICompanyCatalog catalog,
    IStringLocalizer<Resource> resource,
    ILogger<CompaniesDeleteService> logger)
{
    public async Task ExecuteAsync(string code)
    {
        var company = await db.Companies.FirstOrDefaultAsync(c => c.Code == code)
            ?? throw new NotFoundException(resource["COMPANY_NOT_FOUND"].Value);

        if (string.Equals(code, catalog.DefaultCompanyCode, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException("A empresa padrão da instalação não pode ser excluída.");

        if (await db.UserProfiles.AnyAsync(p => p.CompanyCode == code))
            throw new ApplicationException("A empresa tem perfis de usuário atribuídos. Remova-os antes de excluí-la.");

        if (await db.UserSessions.AnyAsync(s => s.CompanyCode == code && s.IsActive && s.ExpiresAt > DateTime.UtcNow))
            throw new ApplicationException("Há sessões ativas nesta empresa. Aguarde ou encerre-as antes de excluí-la.");

        db.Companies.Remove(company);
        await db.SaveChangesAsync();
        catalog.Invalidate();
    }
}
