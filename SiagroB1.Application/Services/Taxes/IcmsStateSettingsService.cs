using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>ICMS por UF: alíquota interna, FCP e regra da base do DIFAL (só STANDALONE; o controller recusa nos demais modos).</summary>
public class IcmsStateSettingsService(IUnitOfWork db)
{
    public IQueryable<IcmsStateSetting> QueryAll() => db.Context.IcmsStateSettings.AsNoTracking();

    public async Task<IcmsStateSetting?> GetByIdAsync(string stateCode) =>
        await db.Context.IcmsStateSettings.FindAsync((stateCode ?? string.Empty).Trim().ToUpperInvariant());

    public async Task<IcmsStateSetting> CreateAsync(IcmsStateSetting entity)
    {
        await NormalizeAsync(entity);

        if (await db.Context.IcmsStateSettings.AnyAsync(s => s.StateCode == entity.StateCode))
            throw new DefaultException($"Já existe configuração de ICMS para a UF {entity.StateCode}; edite a existente.");

        db.Context.IcmsStateSettings.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>A entidade chega rastreada (o controller a carrega e aplica o Delta).</summary>
    public async Task<IcmsStateSetting> UpdateAsync(IcmsStateSetting entity)
    {
        await NormalizeAsync(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<bool> DeleteAsync(string stateCode)
    {
        var entity = await GetByIdAsync(stateCode);

        if (entity is null)
            return false;

        db.Context.IcmsStateSettings.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }

    private async Task NormalizeAsync(IcmsStateSetting entity)
    {
        entity.StateCode = IcmsRateValidation.NormalizeState(entity.StateCode);

        if (!Enum.IsDefined(entity.DifalBaseRule))
            throw new DefaultException("Regra da base do DIFAL inválida.");

        IcmsRateValidation.ValidateRates(entity.InternalRate, entity.FcpRate);

        // Produtos da UF sem FCP próprio herdam o da UF: nenhum pode passar de 100 com o FCP novo.
        var state = entity.StateCode;
        var fcp = entity.FcpRate;
        var offending = await db.Context.IcmsItemStateRates.AsNoTracking()
            .Where(r => r.StateCode == state && r.FcpRate == null && r.InternalRate + fcp >= 100)
            .Select(r => r.ItemCode)
            .FirstOrDefaultAsync();

        if (offending is not null)
            throw new DefaultException(
                $"O produto {offending} herda o FCP da UF {state} e, com este FCP, a soma com a alíquota interna dele chegaria a 100 ou mais.");
    }
}
