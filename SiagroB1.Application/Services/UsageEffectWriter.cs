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

        if (model.StorageDepositRole != StorageDepositRole.None && hasEffect)
        {
            throw new DefaultException(
                "Natureza de remessa ou retorno de depósito não pode alterar o contrato.");
        }

        var direction = model.Direction ?? UsageDirection.Outgoing;

        if (model.StorageDepositRole == StorageDepositRole.Remittance && direction != UsageDirection.Outgoing)
            throw new DefaultException("Natureza de remessa para depósito precisa ser de saída.");

        if (model.StorageDepositRole == StorageDepositRole.Return && direction != UsageDirection.Incoming)
            throw new DefaultException("Natureza de retorno de depósito precisa ser de entrada.");

        if (model.StorageCustodyRole != StorageCustodyRole.None)
        {
            if (hasEffect)
                throw new DefaultException("Natureza de custódia (armazenagem) não pode alterar o contrato.");

            if (model.IsDefault)
                throw new DefaultException("Natureza de custódia (armazenagem) não pode ser a padrão do faturamento de romaneio.");

            if (model.StorageDepositRole != StorageDepositRole.None)
                throw new DefaultException("A natureza tem papel na remessa para depósito ou na custódia, não nos dois.");

            if (model.AllowsCounterNote)
                throw new DefaultException("Natureza de custódia (armazenagem) não permite contra-nota.");

            var incoming = model.StorageCustodyRole is StorageCustodyRole.ProcessingEntry or StorageCustodyRole.StorageEntry;

            if (incoming && direction != UsageDirection.Incoming)
                throw new DefaultException(
                    "Natureza de entrada de custódia (beneficiamento ou armazenagem) precisa ser de entrada.");

            if (!incoming && direction != UsageDirection.Outgoing)
                throw new DefaultException("Natureza de devolução ou retorno de custódia precisa ser de saída.");
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
        effect.StorageDepositRole = model.StorageDepositRole;
        effect.StorageCustodyRole = model.StorageCustodyRole;
        effect.PurchasePurpose = model.PurchasePurpose;

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
