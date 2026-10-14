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
            throw new ApplicationException($"Estorne a baixa do título {provisional.Code} antes de desfazer a composição.");

        await EnqueueAsync(db.Context, composition, userName);
        await db.SaveChangesAsync();
    }

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
        context.FinancialSettlements.Add(new FinancialSettlement
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
