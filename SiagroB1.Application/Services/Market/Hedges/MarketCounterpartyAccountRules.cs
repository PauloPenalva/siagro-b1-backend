using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Market.Hedges;

/// <summary>
/// Normalização e regras de uma conta. Compartilhadas pela gravação avulsa e pela inclusão em profundidade
/// (contraparte + contas), para que as duas vias nunca divirjam.
/// </summary>
public static class MarketCounterpartyAccountRules
{
    /// <summary>Normaliza e valida os campos próprios da conta (sem consultar o banco).</summary>
    public static void Normalize(MarketCounterpartyAccount entity)
    {
        entity.Code = entity.Code?.Trim() ?? string.Empty;
        if (entity.Code.Length == 0)
            throw new BusinessException("Informe o código da conta.");
        if (entity.Code.Length > 30)
            throw new BusinessException("O código da conta deve ter no máximo 30 caracteres.");

        entity.Description = entity.Description?.Trim();
        if (string.IsNullOrEmpty(entity.Description))
            entity.Description = null;
        else if (entity.Description.Length > 100)
            throw new BusinessException("A descrição da conta deve ter no máximo 100 caracteres.");
    }

    public static async Task ValidateAsync(AppDbContext context, MarketCounterpartyAccount entity)
    {
        Normalize(entity);
        if (!await context.MarketCounterparties.AsNoTracking().AnyAsync(c => c.Id == entity.CounterpartyId))
            throw new BusinessException("Corretora ou banco não encontrada.");

        var upper = entity.Code.ToUpper();
        if (await context.MarketCounterpartyAccounts.AsNoTracking().AnyAsync(a =>
                a.Id != entity.Id && a.CounterpartyId == entity.CounterpartyId && a.Code.ToUpper() == upper))
            throw new BusinessException("Já existe uma conta com este código nesta corretora ou banco.");
    }
}
