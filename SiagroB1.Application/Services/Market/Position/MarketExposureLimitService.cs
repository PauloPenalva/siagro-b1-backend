using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Position;

/// <summary>
/// Limites de exposição. Toda gravação exige MARKET_BOARD_APPROVE (diretoria); quem grava e quando é carimbado aqui.
/// As sobrecargas sem usuário da base ficam bloqueadas para ninguém gravar limite sem passar pela permissão.
/// </summary>
public class MarketExposureLimitService(
    AppDbContext context, MarketPermissionGuard guard, ILogger<MarketExposureLimitService> logger)
    : BaseService<MarketExposureLimit, int>(context, logger)
{
    public const string UserRequiredMessage = "Informe o usuário que grava o limite.";

    public override Task<MarketExposureLimit> CreateAsync(MarketExposureLimit entity) =>
        throw new InvalidOperationException(UserRequiredMessage);

    public override Task<MarketExposureLimit?> UpdateAsync(int key, MarketExposureLimit entity) =>
        throw new InvalidOperationException(UserRequiredMessage);

    public override Task<bool> DeleteAsync(int key) => throw new InvalidOperationException(UserRequiredMessage);

    public async Task<MarketExposureLimit> CreateAsync(MarketExposureLimit entity, string userName)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketBoardApprove);
        entity.Id = 0;
        entity.Commodity = null;
        await ValidateAsync(entity, userName);
        return await MarketPersistence.AddAndSaveAsync(context, entity);
    }

    public async Task<MarketExposureLimit?> UpdateAsync(int key, MarketExposureLimit entity, string userName)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketBoardApprove);
        entity.Id = key;
        entity.Commodity = null;
        if (!await context.MarketExposureLimits.AsNoTracking().AnyAsync(l => l.Id == key))
            throw new KeyNotFoundException("Entity not found.");

        await ValidateAsync(entity, userName);
        return await base.UpdateAsync(key, entity);
    }

    public async Task<bool> DeleteAsync(int key, string userName)
    {
        await guard.EnsureAsync(userName, PermissionCodes.MarketBoardApprove);
        var limit = await context.MarketExposureLimits.FirstOrDefaultAsync(l => l.Id == key);
        if (limit is null)
            return false;

        // Os registros de estouro caem junto (cascata); o histórico fica no limite inativo, se for preciso guardá-lo.
        var breaches = await context.MarketLimitBreaches.Where(b => b.LimitId == key).ToListAsync();
        context.MarketLimitBreaches.RemoveRange(breaches);
        context.MarketExposureLimits.Remove(limit);
        await context.SaveChangesAsync();
        return true;
    }

    private async Task ValidateAsync(MarketExposureLimit entity, string userName)
    {
        if (!Enum.IsDefined(entity.Scope))
            throw new BusinessException("Informe o tipo do limite (commodity ou dólar).");

        entity.CommodityCode = string.IsNullOrWhiteSpace(entity.CommodityCode) ? null : entity.CommodityCode.Trim();
        if (entity.Scope == MarketLimitScope.Commodity)
        {
            if (entity.CommodityCode is null)
                throw new BusinessException("Informe a commodity do limite.");
            if (!await MarketCommodityRules.CommodityExistsAsync(context, entity.CommodityCode))
                throw new BusinessException("Commodity não encontrada.");
        }
        else if (entity.CommodityCode is not null)
        {
            throw new BusinessException("O limite em dólar não tem commodity.");
        }

        if (entity.Year.HasValue != entity.Month.HasValue)
            throw new BusinessException("Informe o ano e o mês juntos, ou deixe os dois vazios para o limite total.");
        if (entity.Year is { } year && year is < 2000 or > 2100)
            throw new BusinessException("O ano do limite deve estar entre 2000 e 2100.");
        if (entity.Month is { } month && month is < 1 or > 12)
            throw new BusinessException("O mês do limite deve estar entre 1 e 12.");

        // Arredonda à escala da coluna antes de validar: 0,004 viraria 0 no banco.
        entity.MaxLong = Scale(entity.MaxLong);
        entity.MaxShort = Scale(entity.MaxShort);
        if (entity.MaxLong is null && entity.MaxShort is null)
            throw new BusinessException("Informe o limite comprado, o limite vendido ou os dois.");
        if (entity.MaxLong <= 0 || entity.MaxShort <= 0)
            throw new BusinessException("Os limites devem ser positivos; o limite vendido é informado sem sinal.");

        if (entity.Active)
        {
            var scope = entity.Scope;
            var code = entity.CommodityCode;
            var y = entity.Year;
            var m = entity.Month;
            if (await context.MarketExposureLimits.AsNoTracking().AnyAsync(l =>
                    l.Id != entity.Id && l.Active && l.Scope == scope && l.CommodityCode == code
                    && l.Year == y && l.Month == m))
                throw new BusinessException("Já existe um limite ativo para este escopo, commodity e período.");
        }

        entity.UpdatedBy = userName;
        entity.UpdatedAt = DateTime.UtcNow;
    }

    private static decimal? Scale(decimal? value) =>
        value is { } v ? Math.Round(v, 2, MidpointRounding.AwayFromZero) : null;
}
