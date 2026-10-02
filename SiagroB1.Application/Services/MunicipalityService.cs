using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>Municípios do IBGE, só leitura (pesquisa das telas de filial e parceiro).</summary>
public class MunicipalityService(IUnitOfWork db)
{
    public IQueryable<Municipality> QueryAll() => db.Context.Municipalities.AsNoTracking();

    public Task<Municipality?> GetByIdAsync(string code) =>
        db.Context.Municipalities.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code);
}
