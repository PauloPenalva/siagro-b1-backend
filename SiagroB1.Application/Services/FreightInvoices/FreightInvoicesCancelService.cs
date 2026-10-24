using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>
/// Cancela a fatura de frete. Rascunho: só solta os CT-e. Emitida: desfaz a aglutinação — cancela o(s) título(s) da
/// fatura (sem 505: origem FreightInvoice fica fora da contabilização) e grava o estorno (linha negativa espelho) de cada
/// baixa Aggregation, reabrindo os títulos dos CT-e. Faz direto o que as telas do Financeiro recusam para essas origens.
/// Recusa se algum título da fatura tem baixa. Cancelar de novo é recusado ("já está cancelada").
/// Tudo numa transação, um SaveChanges; erro → Rollback + ChangeTracker.Clear (InMemory não desfaz).
/// </summary>
public class FreightInvoicesCancelService(IUnitOfWork db, IConfiguration configuration)
{
    public const string HasSettlement = "A fatura possui baixa; estorne a baixa antes de cancelar.";

    public async Task CancelAsync(Guid key, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);

        await db.BeginTransactionAsync();
        try
        {
            await CancelCoreAsync(key, user);
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            db.Context.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task CancelCoreAsync(Guid key, string user)
    {
        var context = db.Context;
        var invoice = await context.FreightInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new DefaultException("Fatura de frete não encontrada.");
        if (invoice.Status == FreightInvoiceStatus.Cancelled)
            throw new DefaultException("A fatura já está cancelada.");

        if (invoice.Status != FreightInvoiceStatus.Draft)
        {
            var titles = await context.FinancialDocuments
                .Where(d => d.OriginType == FinancialDocumentOrigin.FreightInvoice && d.OriginKey == key
                            && d.Status != FinancialDocumentStatus.Canceled)
                .ToListAsync();
            if (titles.Any(t => t.SettledAmount != 0m || t.ComposedAmount != 0m))
                throw new DefaultException(HasSettlement);

            var reason = $"Fatura de frete {invoice.Code} cancelada";
            foreach (var title in titles)
                FinancialDocumentsCancelService.Cancel(title, reason, user);

            // [settlement-posting: not applicable] — estorno da aglutinação: nada foi lançado, nada a estornar.
            var aggregations = await context.FinancialSettlements
                .Where(s => s.Origin == FinancialSettlementOrigin.Aggregation && s.RelatedDocumentKey == key
                            && !context.FinancialSettlements.Any(r => r.ReversedSettlementKey == s.Key))
                .ToListAsync();
            foreach (var settlement in aggregations)
                context.FinancialSettlements.Add(FinancialDocumentsReverseSettlementService.Mirror(settlement, reason, user));
            foreach (var documentKey in aggregations.Select(s => s.FinancialDocumentKey).Distinct())
                await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(context, documentKey);
        }

        foreach (var item in invoice.Items) item.IsActive = false;
        invoice.Status = FreightInvoiceStatus.Cancelled;
    }
}
