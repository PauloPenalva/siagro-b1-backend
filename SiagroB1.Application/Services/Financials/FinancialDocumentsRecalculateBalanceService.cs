using SiagroB1.Application.Services.StorageInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// ESCRITOR ÚNICO de <see cref="FinancialDocument.SettledAmount"/> e de
/// <see cref="FinancialDocument.Status"/>.
///
/// O saldo é sempre derivado da SOMA do ledger, nunca incremental: é a mesma regra de
/// SalesContractsRecalculateBalanceService, e é o que faz baixa, estorno e (nas fases
/// seguintes) abatimento e compensação conviverem sem divergir.
///
/// A única exceção ao "escritor único" é <see cref="FinancialDocumentStatus.Canceled"/>, que
/// só o cancelamento grava e que este serviço NUNCA sobrescreve.
/// </summary>
public class FinancialDocumentsRecalculateBalanceService(IUnitOfWork db)
{
    /// <summary>
    /// Recalcula e apenas ENFILEIRA a alteração — quem chama decide quando salvar.
    ///
    /// Soma o ledger do BANCO mais as linhas ainda PENDENTES no ChangeTracker (Added soma; Deleted ainda está no banco,
    /// então subtrai). É o que deixa composição, desfazer, encontro de contas e o firme recém-nascido num SaveChanges
    /// só, sem salvar no meio. Ledger e composição não têm valor alterado em linha existente (insert-only/remove), então
    /// Modified não entra. Depois do SaveChanges não há pendência e o resultado é o mesmo de sempre.
    /// </summary>
    public static async Task RecalculateAsync(AppDbContext context, Guid documentKey)
    {
        // FindAsync, e não uma consulta: enxerga também o título ainda não salvo.
        var document = await context.FinancialDocuments.FindAsync(documentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        if (document.Nature == FinancialDocumentNature.Advance)
        {
            // Spec 2026-10-14 (compensação de adiantamento): dois eixos. Aplicado = compensações e transferências de
            // SAÍDA (e os estornos delas); pago = todo o resto, inclusive a transferência de ENTRADA.
            var lines = await LedgerAsync(context, documentKey);
            var byKey = lines.ToDictionary(x => x.Key);
            var applied = lines.Where(x => AdvanceSettlementAxis.CountsAsApplied(document, x, byKey)).Sum(x => x.Amount);

            document.AppliedAmount = decimal.Round(applied, 2, MidpointRounding.ToEven);
            document.SettledAmount = decimal.Round(lines.Sum(x => x.Amount) - applied, 2, MidpointRounding.ToEven);
        }
        else
        {
            var settled = (await context.FinancialSettlements
                              .Where(x => x.FinancialDocumentKey == documentKey)
                              .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                          + PendingDelta(context.ChangeTracker.Entries<FinancialSettlement>()
                              .Where(e => e.Entity.FinancialDocumentKey == documentKey), x => x.Amount);

            document.SettledAmount = decimal.Round(settled, 2, MidpointRounding.ToEven);
        }

        // Spec 2026-10-13 (títulos firmes): só o provisório tem composição.
        if (document.Nature == FinancialDocumentNature.Provisional)
        {
            var composed = (await context.FinancialDocumentCompositions
                               .Where(x => x.ProvisionalDocumentKey == documentKey)
                               .SumAsync(x => (decimal?)x.Amount) ?? 0m)
                           + PendingDelta(context.ChangeTracker.Entries<FinancialDocumentComposition>()
                               .Where(e => e.Entity.ProvisionalDocumentKey == documentKey), x => x.Amount);

            document.ComposedAmount = decimal.Round(composed, 2, MidpointRounding.ToEven);
        }

        if (document.Status == FinancialDocumentStatus.Canceled)
            return;

        // Documento sem valor nao tem o que liquidar: nasce e permanece quitado. Sem isto, o
        // braco `<= 0` da expressao abaixo o classificaria como Aberto para sempre, e ele ficaria
        // entulhando a tela de Contas a Pagar com saldo zero.
        // Regra 16: o provisório só fica Settled quando a baixa alcança o valor CONTRATADO (NetAmount).
        document.Status = document.NetAmount <= 0m
            ? FinancialDocumentStatus.Settled
            : document.SettledAmount switch
            {
                <= 0m => FinancialDocumentStatus.Open,
                var s when s >= document.NetAmount => FinancialDocumentStatus.Settled,
                _ => FinancialDocumentStatus.PartiallySettled
            };

        // Spec 2026-10-19 parte 3: baixar/estornar o título a receber de um documento que fatura a fatura de armazenagem
        // muda a situação dela (Faturada ↔ Paga). Ponto único: toda baixa, estorno, encontro e compensação passa aqui.
        if (document.Direction == FinancialDirection.Receivable)
            await StorageInvoicesRecalculateStatusService.RecalculateForSalesInvoiceAsync(context, document.SalesInvoiceKey);

        // Fatura de frete: baixar/estornar o título dela muda a situação (Emitida ↔ Liquidada). Mesmo ponto único.
        if (document.OriginType == FinancialDocumentOrigin.FreightInvoice && document.OriginKey is { } freightInvoiceKey)
            await SiagroB1.Application.Services.FreightInvoices.FreightInvoicesRefreshStatusService.RefreshAsync(context, freightInvoiceKey);
    }

    /// <summary>
    /// Linhas do ledger do título como ficarão depois do SaveChanges: as do banco menos as Deleted pendentes, mais as
    /// Added pendentes. Ledger é insert-only, então Modified não entra.
    /// </summary>
    private static async Task<List<FinancialSettlement>> LedgerAsync(AppDbContext context, Guid documentKey)
    {
        var pending = context.ChangeTracker.Entries<FinancialSettlement>()
            .Where(e => e.Entity.FinancialDocumentKey == documentKey)
            .ToList();
        var deleted = pending.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Key).ToHashSet();

        var stored = await context.FinancialSettlements.AsNoTracking()
            .Where(x => x.FinancialDocumentKey == documentKey)
            .ToListAsync();

        return stored.Where(x => !deleted.Contains(x.Key))
            .Concat(pending.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .ToList();
    }

    /// <inheritdoc cref="AdvanceSettlementAxis.IsIncomingTransfer"/>
    internal static bool IsIncomingTransfer(FinancialDocument advance, FinancialSettlement line) =>
        AdvanceSettlementAxis.IsIncomingTransfer(advance, line);

    private static decimal PendingDelta<T>(IEnumerable<EntityEntry<T>> entries, Func<T, decimal> amount) where T : class =>
        entries.Sum(e => e.State switch
        {
            EntityState.Added => amount(e.Entity),
            EntityState.Deleted => -amount(e.Entity),
            _ => 0m,
        });

    public async Task<FinancialDocumentRecalcResultDto> ExecuteAsync(Guid key)
    {
        await RecalculateAsync(db.Context, key);
        await db.SaveChangesAsync();

        var document = await db.Context.FinancialDocuments.AsNoTracking()
                           .FirstAsync(x => x.Key == key);

        return new FinancialDocumentRecalcResultDto
        {
            Key = document.Key,
            Code = document.Code,
            NetAmount = document.NetAmount,
            SettledAmount = document.SettledAmount,
            OpenAmount = document.OpenAmount,
            Status = document.Status.ToString()
        };
    }
}
