using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Desfaz UMA linha de composição (spec 2026-10-13, títulos firmes, regra 13): grava no título composto o estorno
/// espelho do abatimento, REMOVE a linha de composição, recalcula os dois e loga nos dois. O estorno genérico recusa
/// abatimento (regra 14) — este é o único caminho.
/// </summary>
public class FinancialDocumentsUncomposeService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid compositionKey, string userName)
    {
        var composition = await db.Context.FinancialDocumentCompositions.FirstOrDefaultAsync(x => x.Key == compositionKey)
                          ?? throw new NotFoundException("Composição não encontrada.");

        var provisional = await db.Context.FinancialDocuments.FirstAsync(x => x.Key == composition.ProvisionalDocumentKey);

        // Sem a linha, o composto não pode ficar abaixo do que já foi pago no provisório.
        if (provisional.ComposedAmount - composition.Amount < provisional.SettledAmount)
        {
            if (provisional.SettledAmount > 0m)
                throw new ApplicationException($"Estorne a baixa do título {provisional.Code} antes de desfazer a composição.");

            // Nada pago: quem deixaria o composto negativo é uma devolução composta neste provisório (revisão final I5).
            throw new ApplicationException(
                ReturnFirstRefusal(await ComposedReturnCodeAsync(db.Context, provisional.Key, [composition.Key])));
        }

        await EnqueueAsync(db.Context, composition, userName);
        await db.SaveChangesAsync();
    }

    /// <summary>Regra 13 sem baixa: a devolução composta no provisório tem de sair antes do firme.</summary>
    internal static string ReturnFirstRefusal(string? returnCode) =>
        $"Desfaça antes a composição da devolução {returnCode} neste título.";

    /// <summary>Código da devolução (linha de composição negativa) que fica no provisório, fora das linhas que saem.</summary>
    internal static async Task<string?> ComposedReturnCodeAsync(
        AppDbContext context, Guid provisionalKey, IReadOnlyCollection<Guid> leavingCompositionKeys) =>
        await context.FinancialDocumentCompositions.AsNoTracking()
            .Where(c => c.ProvisionalDocumentKey == provisionalKey && c.Amount < 0m && !leavingCompositionKeys.Contains(c.Key))
            .Join(context.FinancialDocuments, c => c.ComposedDocumentKey, d => d.Key, (c, d) => d.Code)
            .OrderBy(code => code)
            .FirstOrDefaultAsync();

    /// <summary>
    /// Enqueue-only e SEM a checagem da regra 13: o estorno de romaneio (FinancialDocumentsFirmUnwind) só chega aqui
    /// depois de garantir que o provisório não tem baixa.
    /// </summary>
    public static async Task EnqueueAsync(AppDbContext context, FinancialDocumentComposition composition, string userName)
    {
        var offset = await context.FinancialSettlements.FirstOrDefaultAsync(x =>
                         x.CompositionKey == composition.Key && x.Origin == FinancialSettlementOrigin.InvoiceOffset)
                     ?? throw new ApplicationException("Abatimento da composição não encontrado.");

        var provisional = await context.FinancialDocuments.FindAsync(composition.ProvisionalDocumentKey)
                          ?? throw new NotFoundException("Documento financeiro não encontrado.");
        var composed = await context.FinancialDocuments.FindAsync(composition.ComposedDocumentKey)
                       ?? throw new NotFoundException("Documento financeiro não encontrado.");

        var now = DateTime.Now;
        context.FinancialSettlements.Add(new FinancialSettlement // [settlement-posting: not applicable] composição não é pagamento
        {
            Key = Guid.NewGuid(),
            FinancialDocumentKey = offset.FinancialDocumentKey,
            FinancialAccountCode = null,
            SettlementDate = now.Date,
            Amount = -offset.Amount,
            Origin = FinancialSettlementOrigin.Reversal,
            ReversedSettlementKey = offset.Key,
            RelatedDocumentKey = composition.ProvisionalDocumentKey,
            CompositionKey = composition.Key,
            DocumentReference = offset.DocumentReference,
            Notes = "Composição desfeita",
            CreatedAt = now, CreatedBy = userName, UpdatedAt = now, UpdatedBy = userName,
        });

        context.FinancialDocumentCompositions.Remove(composition);

        var changeLog = new FinancialDocumentChangeLogService(context);
        changeLog.Register(provisional.Key, FinancialDocumentChangeLogFields.Composition,
            FinancialDocumentsComposeService.Describe(composed.Code, composition.Amount), null, userName);
        changeLog.Register(composed.Key, FinancialDocumentChangeLogFields.Composition,
            FinancialDocumentsComposeService.Describe(provisional.Code, composition.Amount), null, userName);

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, provisional.Key);
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, composed.Key);
    }
}
