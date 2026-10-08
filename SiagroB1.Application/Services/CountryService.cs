using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>Países, só leitura (value help do endereço do parceiro).</summary>
public class CountryService(IUnitOfWork db)
{
    public IQueryable<Country> QueryAll() => db.Context.Countries.AsNoTracking();

    public Task<Country?> GetByIdAsync(string code) =>
        db.Context.Countries.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code);
}
