using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Grava o efeito de negócio de uma natureza de operação em USAGE_EFFECTS.
///
/// Compartilhado pelos DOIS modos de propósito: o efeito é sempre do Siagro, venha a
/// identidade de USAGES (STANDALONE) ou de OUSG (SAPB1). É isto que destrava o modo SAPB1 —
/// antes o efeito morava na mesma linha da identidade, que lá é do ERP e não se escreve.
///
/// Não faz SaveChanges: quem é dono da transação é o serviço chamador.
/// </summary>
internal static class UsageEffectWriter
{
    /// <summary>
    /// Efeito no contrato sem exigir contrato é configuração impossível: a linha do ledger
    /// tem <c>SalesContractKey</c> não anulável, então o documento não teria onde aplicar o
    /// efeito. Barrar no cadastro evita a natureza que só falha na hora de confirmar.
    /// </summary>
    internal static void ValidateEffects(UsageModel model)
    {
        var hasEffect = model.ContractBalanceEffect != ContractBalanceEffect.None
                        || model.ContractValueEffect != ContractValueEffect.None;

        if (hasEffect && !model.RequiresContract)
        {
            throw new DefaultException(
                "Natureza de operação com efeito no contrato precisa exigir contrato.");
        }
    }

    internal static async Task WriteAsync(IUnitOfWork db, int usageCode, UsageModel model)
    {
        var effect = await db.Context.UsageEffects
                         .FirstOrDefaultAsync(x => x.UsageCode == usageCode);

        if (effect == null)
        {
            effect = new UsageEffect { UsageCode = usageCode };
            await db.Context.UsageEffects.AddAsync(effect);
        }

        effect.ContractBalanceEffect = model.ContractBalanceEffect;
        effect.ContractValueEffect = model.ContractValueEffect;
        effect.RequiresContract = model.RequiresContract;
        effect.RequiresQuantity = model.RequiresQuantity;
        effect.RequiresWeight = model.RequiresWeight;
        effect.IsDefault = model.IsDefault;

        await ClearOtherDefaultsAsync(db, usageCode, model.IsDefault);
    }

    /// <summary>
    /// Só pode haver UMA natureza padrão: o faturamento de romaneio resolve a dele por essa
    /// flag, e duas candidatas dariam documento com natureza imprevisível. A invariante é
    /// mantida aqui e não por índice no banco porque o padrão vale por instalação, não por
    /// modo. Marcar uma natureza como padrão desmarca a anterior — é o comportamento que a
    /// tela precisa, e evita travar o usuário com "já existe um padrão".
    /// </summary>
    private static async Task ClearOtherDefaultsAsync(IUnitOfWork db, int usageCode, bool isDefault)
    {
        if (!isDefault)
        {
            return;
        }

        var others = await db.Context.UsageEffects
            .Where(x => x.IsDefault && x.UsageCode != usageCode)
            .ToListAsync();

        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }
}
