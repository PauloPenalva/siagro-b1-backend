using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Interceptors;

/// <summary>
/// Impede alterar (add/update/delete) sub-entidades vinculadas a um contrato encerrado
/// (<see cref="ContractStatus.Finished"/>). Compra: corretores, impostos, parâmetros de qualidade, fixações de preço
/// e washouts. Venda: washouts (spec 2026-10-15). Cobre todos os caminhos de mutação num único ponto.
/// </summary>
public class FinishedContractMutationGuardInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var (purchaseKeys, salesKeys) = CollectContractKeys(eventData.Context);

            if (purchaseKeys.Count > 0 && await eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .AnyAsync(c => purchaseKeys.Contains(c.Key) && c.Status == ContractStatus.Finished, cancellationToken))
                throw Blocked();

            if (salesKeys.Count > 0 && await eventData.Context.Set<SalesContract>()
                    .AsNoTracking()
                    .AnyAsync(c => salesKeys.Contains(c.Key) && c.Status == ContractStatus.Finished, cancellationToken))
                throw Blocked();
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            var (purchaseKeys, salesKeys) = CollectContractKeys(eventData.Context);

            if (purchaseKeys.Count > 0 && eventData.Context.Set<PurchaseContract>()
                    .AsNoTracking()
                    .Any(c => purchaseKeys.Contains(c.Key) && c.Status == ContractStatus.Finished))
                throw Blocked();

            if (salesKeys.Count > 0 && eventData.Context.Set<SalesContract>()
                    .AsNoTracking()
                    .Any(c => salesKeys.Contains(c.Key) && c.Status == ContractStatus.Finished))
                throw Blocked();
        }

        return base.SavingChanges(eventData, result);
    }

    private static (HashSet<Guid> Purchase, HashSet<Guid> Sales) CollectContractKeys(DbContext context)
    {
        var purchase = new HashSet<Guid>();
        var sales = new HashSet<Guid>();

        // ⚠️ NÃO acrescente FinancialDocument a este switch, por mais que pareça "seguir o
        // padrão". O encerramento do contrato cancela o provisório no MESMO SaveChanges em que
        // grava Finished; incluído aqui, o próprio encerramento passaria a lançar
        // "Contrato encerrado: não é possível alterar dados vinculados ao contrato.", sem saída
        // pela tela — e qualquer baixa de documento de contrato encerrado também.
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            // PurchaseContractComment está fora de propósito: comentário é anotação, não altera
            // valor nem saldo, e o requisito é justamente poder comentar contrato encerrado.
            // ContractDraft / ContractDraftSigner ficam FORA de propósito: minuta é documentação,
            // como anexo — um contrato encerrado pode ter aditivo ou distrato (spec 2026-09-21).
            Guid? purchaseKey = entry.Entity switch
            {
                PurchaseContractBroker b => b.PurchaseContractKey,
                PurchaseContractTax t => t.PurchaseContractKey,
                PurchaseContractQualityParameter q => q.PurchaseContractKey,
                PurchaseContractPriceFixation f => f.PurchaseContractKey,
                PurchaseContractWashout w => w.PurchaseContractKey,
                _ => null
            };

            if (purchaseKey.HasValue && purchaseKey.Value != Guid.Empty)
                purchase.Add(purchaseKey.Value);

            // Venda: só o washout (spec 2026-10-15). Os demais filhos de venda nunca passaram por esta guarda.
            if (entry.Entity is SalesContractWashout sw && sw.SalesContractKey != Guid.Empty)
                sales.Add(sw.SalesContractKey);
        }

        return (purchase, sales);
    }

    private static DefaultException Blocked() =>
        new("Contrato encerrado: não é possível alterar dados vinculados ao contrato.");
}
