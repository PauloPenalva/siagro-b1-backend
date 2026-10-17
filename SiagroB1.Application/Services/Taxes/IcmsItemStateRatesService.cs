using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>Alíquota interna do ICMS por produto e UF (só STANDALONE; o controller recusa nos demais modos).</summary>
public class IcmsItemStateRatesService(IUnitOfWork db)
{
    public IQueryable<IcmsItemStateRate> QueryAll() => db.Context.IcmsItemStateRates.AsNoTracking();

    public async Task<IcmsItemStateRate?> GetByIdAsync(Guid key) =>
        await db.Context.IcmsItemStateRates.FindAsync(key);

    public async Task<IcmsItemStateRate> CreateAsync(IcmsItemStateRate entity)
    {
        Normalize(entity);

        if (entity.Key == Guid.Empty)
            entity.Key = Guid.NewGuid();

        if (await db.Context.IcmsItemStateRates.AnyAsync(r => r.ItemCode == entity.ItemCode && r.StateCode == entity.StateCode))
            throw new DefaultException($"Já existe alíquota do produto {entity.ItemCode} para a UF {entity.StateCode}; edite a existente.");

        db.Context.IcmsItemStateRates.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>A entidade chega rastreada (o controller a carrega e aplica o Delta).</summary>
    public async Task<IcmsItemStateRate> UpdateAsync(IcmsItemStateRate entity)
    {
        Normalize(entity);

        if (await db.Context.IcmsItemStateRates.AnyAsync(r =>
                r.Key != entity.Key && r.ItemCode == entity.ItemCode && r.StateCode == entity.StateCode))
            throw new DefaultException($"Já existe alíquota do produto {entity.ItemCode} para a UF {entity.StateCode}.");

        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<bool> DeleteAsync(Guid key)
    {
        var entity = await db.Context.IcmsItemStateRates.FindAsync(key);

        if (entity is null)
            return false;

        db.Context.IcmsItemStateRates.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }

    private static void Normalize(IcmsItemStateRate entity)
    {
        entity.ItemCode = (entity.ItemCode ?? string.Empty).Trim();

        if (entity.ItemCode.Length == 0)
            throw new DefaultException("Informe o produto.");

        entity.StateCode = IcmsRateValidation.NormalizeState(entity.StateCode);
        IcmsRateValidation.ValidateRates(entity.InternalRate, entity.FcpRate);
    }
}
