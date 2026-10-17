using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WithholdingTaxSettings;

/// <summary>Credor e dia de vencimento de cada tributo retido (só STANDALONE; o controller recusa nos demais modos).</summary>
public class WithholdingTaxSettingsService(IUnitOfWork db, IBusinessPartnerService businessPartners)
{
    public IQueryable<WithholdingTaxSetting> QueryAll() => db.Context.WithholdingTaxSettings.AsNoTracking();

    public async Task<WithholdingTaxSetting?> GetByIdAsync(int taxType) =>
        await db.Context.WithholdingTaxSettings.FindAsync(taxType);

    public async Task<WithholdingTaxSetting> CreateAsync(WithholdingTaxSetting entity)
    {
        await NormalizeAsync(entity);

        if (await db.Context.WithholdingTaxSettings.AnyAsync(s => s.TaxType == entity.TaxType))
            throw new DefaultException($"Já existe configuração para o tributo {Label((WithholdingTaxType)entity.TaxType)}; edite a existente.");

        db.Context.WithholdingTaxSettings.Add(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    /// <summary>A entidade chega rastreada (o controller a carrega e aplica o Delta).</summary>
    public async Task<WithholdingTaxSetting> UpdateAsync(WithholdingTaxSetting entity)
    {
        await NormalizeAsync(entity);
        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<bool> DeleteAsync(int taxType)
    {
        var entity = await db.Context.WithholdingTaxSettings.FindAsync(taxType);

        if (entity is null)
            return false;

        db.Context.WithholdingTaxSettings.Remove(entity);
        await db.SaveChangesAsync();

        return true;
    }

    private async Task NormalizeAsync(WithholdingTaxSetting entity)
    {
        if (!Enum.IsDefined(typeof(WithholdingTaxType), entity.TaxType))
            throw new DefaultException("Tributo inválido.");

        if (entity.DueDay is < 1 or > 31)
            throw new DefaultException("O dia de vencimento deve estar entre 1 e 31.");

        var cardCode = (entity.CardCode ?? string.Empty).Trim();
        if (cardCode.Length == 0)
            throw new DefaultException("Informe o credor do tributo.");

        var partner = await businessPartners.GetByIdAsync(cardCode)
                      ?? throw new DefaultException($"O parceiro {cardCode} não existe.");

        entity.CardCode = cardCode;
        entity.CardName = partner.CardName;
    }

    private static string Label(WithholdingTaxType taxType) => taxType.ToString().ToUpperInvariant();
}
