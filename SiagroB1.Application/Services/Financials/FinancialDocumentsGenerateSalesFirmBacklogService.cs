using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

/// <summary>
/// Spec 2026-10-15, regra 19: documento de saída confirmado ANTES do deploy não tem firme, e sem ele o contrato em
/// andamento não consegue quitar o provisório. Mesmo molde de <see cref="FinancialDocumentsGenerateBacklogService"/>:
/// deliberado, por filial e janela, com DryRun. Só linhas SEM título ativo são sincronizadas, uma por SaveChanges, na
/// ordem do documento (a origem ganha firme antes da devolução que a compensa). Fora de transação.
/// </summary>
public class FinancialDocumentsGenerateSalesFirmBacklogService(IUnitOfWork db, FinancialDocumentsSalesFirmSyncService sync)
{
    public async Task<SalesFirmBacklogResultDto> ExecuteAsync(
        string? branchCode, DateTime fromDate, DateTime toDate, bool dryRun, string userName)
    {
        var context = db.Context;
        var result = new SalesFirmBacklogResultDto { DryRun = dryRun };

        var lines = await context.SalesInvoicesItems.AsNoTracking()
            .Where(i => (i.SalesInvoice!.InvoiceStatus == InvoiceStatus.Confirmed ||
                         i.SalesInvoice.InvoiceStatus == InvoiceStatus.Returned) &&
                        i.SalesInvoice.InvoiceDate >= fromDate && i.SalesInvoice.InvoiceDate <= toDate &&
                        (branchCode == null || i.SalesInvoice.BranchCode == branchCode) &&
                        i.SalesContractKey != null && i.SalesContract!.Status == ContractStatus.Approved)
            .OrderBy(i => i.SalesInvoice!.InvoiceDate).ThenBy(i => i.SalesInvoice!.InvoiceNumber)
            .Select(i => i.Key)
            .ToListAsync();

        foreach (var key in lines.Where(k => k != null).Select(k => k!.Value))
        {
            result.EligibleLines++;

            var hasTitle = await context.FinancialDocuments.AnyAsync(d =>
                d.OriginType == FinancialDocumentOrigin.SalesInvoice && d.OriginKey == key &&
                d.Status != FinancialDocumentStatus.Canceled);
            if (hasTitle)
            {
                result.SkippedAlreadyGenerated++;
                continue;
            }

            result.LinesWithoutFirm++;
            if (dryRun) continue;

            await sync.EnqueueSyncAsync([key], userName);
            result.Generated += context.ChangeTracker.Entries<FinancialDocument>().Count(e => e.State == EntityState.Added);
            await db.SaveChangesAsync();
        }

        return result;
    }
}
