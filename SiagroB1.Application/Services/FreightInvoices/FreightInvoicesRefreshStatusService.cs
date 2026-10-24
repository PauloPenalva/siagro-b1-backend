using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>
/// Situação da fatura de frete emitida: Liquidada quando todos os títulos ativos dela estão sem saldo; volta a Emitida se
/// uma baixa for estornada. Rascunho e cancelada não mudam. Chamado pelo ponto único do ledger
/// (<see cref="Financials.FinancialDocumentsRecalculateBalanceService.RecalculateAsync"/>) — só ENFILEIRA, quem chama salva.
/// </summary>
public class FreightInvoicesRefreshStatusService(IUnitOfWork db)
{
    public async Task RefreshAsync(Guid invoiceKey)
    {
        await RefreshAsync(db.Context, invoiceKey);
        await db.SaveChangesAsync();
    }

    public static async Task RefreshAsync(AppDbContext context, Guid invoiceKey)
    {
        var invoice = await context.FreightInvoices.FindAsync(invoiceKey);
        if (invoice is null || invoice.Status is not (FreightInvoiceStatus.Issued or FreightInvoiceStatus.Settled))
            return;

        // Consulta rastreada devolve as instâncias já em memória (valores recalculados ainda não salvos); somam-se as
        // parcelas recém-adicionadas, que a consulta não enxerga.
        var stored = await context.FinancialDocuments
            .Where(d => d.OriginType == FinancialDocumentOrigin.FreightInvoice && d.OriginKey == invoiceKey)
            .ToListAsync();
        var titles = stored
            .Concat(context.ChangeTracker.Entries<FinancialDocument>()
                .Where(e => e.State == EntityState.Added && e.Entity.OriginType == FinancialDocumentOrigin.FreightInvoice
                                                         && e.Entity.OriginKey == invoiceKey)
                .Select(e => e.Entity))
            .DistinctBy(d => d.Key)
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
        if (titles.Count == 0) return;

        invoice.Status = titles.All(t => t.OpenAmount == 0m) ? FreightInvoiceStatus.Settled : FreightInvoiceStatus.Issued;
    }
}
