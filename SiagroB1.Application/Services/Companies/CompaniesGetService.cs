using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Common;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Companies;

/// <summary>A string de conexão sai sempre com a senha mascarada.</summary>
public class CompaniesGetService(CommonDbContext db)
{
    public async Task<Company?> GetByIdAsync(string code)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code);
        return company is null ? null : Masked(company);
    }

    /// <summary>Poucas linhas: materializa e mascara em memória (o $filter do OData passa a ser aplicado em memória).</summary>
    public IQueryable<Company> QueryAll() =>
        db.Companies.AsNoTracking().AsEnumerable().Select(Masked).AsQueryable();

    private static Company Masked(Company c) => new()
    {
        Code = c.Code,
        CompanyName = c.CompanyName,
        ConnectionString = CompanyConnectionStrings.MaskPassword(c.ConnectionString),
        SapCompanyDb = c.SapCompanyDb,
    };
}
