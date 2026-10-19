using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageInvoices;

/// <summary>
/// Situação da fatura de armazenagem (spec 2026-10-19 parte 3), derivada — escritor único do Status fora do cancelamento:
/// <list type="bullet">
/// <item>Cancelada fica; Paga sem documento vinculado é legado (o antigo "Encerrada") e fica.</item>
/// <item>Faturado = Σ GrandTotal das linhas dos documentos vinculados Confirmados/Devolvidos. Zero → Aberta;
/// abaixo do total → Faturada parcial.</item>
/// <item>No total: Paga quando há título a receber ativo e todos estão baixados; senão Faturada. Em SAPB1 o título é local
/// (decisão de 19/10); sem título no Siagro a fatura para em Faturada.</item>
/// </list>
/// Lê o rastreador (documentos e títulos alterados no mesmo SaveChanges do chamador): chame depois de aplicar as mudanças e
/// antes de salvar. Não grava.
/// </summary>
public static class StorageInvoicesRecalculateStatusService
{
    public static async Task RecalculateAsync(AppDbContext context, Guid storageInvoiceKey)
    {
        var invoice = await context.StorageInvoices.FindAsync(storageInvoiceKey)
                      ?? throw new NotFoundException("Fatura de armazenagem não encontrada.");
        if (invoice.Status == StorageInvoiceStatus.Cancelled) return;

        var documents = await TrackedAsync(context.SalesInvoices.Include(i => i.Items)
            .Where(i => i.StorageInvoiceKey == storageInvoiceKey), context, i => i.StorageInvoiceKey == storageInvoiceKey);
        if (documents.Count == 0 && invoice.Status == StorageInvoiceStatus.Paid) return;

        var billed = documents
            .Where(d => d.InvoiceStatus is InvoiceStatus.Confirmed or InvoiceStatus.Returned)
            .ToList();
        var invoiced = billed.Sum(d => d.Items.Sum(l => l.GrandTotal));

        invoice.Status = invoiced <= 0m ? StorageInvoiceStatus.Open
            : invoiced < invoice.TotalAmount ? StorageInvoiceStatus.PartiallyInvoiced
            : await AllPaidAsync(context, billed.Select(d => d.Key).ToList()) ? StorageInvoiceStatus.Paid
            : StorageInvoiceStatus.Invoiced;
    }

    private static async Task<bool> AllPaidAsync(AppDbContext context, List<Guid> salesInvoiceKeys)
    {
        var titles = (await TrackedAsync(context.FinancialDocuments
                    .Where(d => d.SalesInvoiceKey != null && salesInvoiceKeys.Contains(d.SalesInvoiceKey.Value) &&
                                d.Direction == FinancialDirection.Receivable),
                context, d => d.SalesInvoiceKey != null && salesInvoiceKeys.Contains(d.SalesInvoiceKey.Value) &&
                              d.Direction == FinancialDirection.Receivable))
            .Where(d => d.Status != FinancialDocumentStatus.Canceled)
            .ToList();
        return titles.Count > 0 && titles.All(d => d.Status == FinancialDocumentStatus.Settled);
    }

    /// <summary>Gravado (rastreado, com o valor em memória) + Added ainda não salvo − Deleted.</summary>
    private static async Task<List<T>> TrackedAsync<T>(IQueryable<T> query, AppDbContext context, Func<T, bool> match)
        where T : class
    {
        var saved = await query.ToListAsync();
        var entries = context.ChangeTracker.Entries<T>().Where(e => match(e.Entity)).ToList();
        var deleted = entries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity).ToHashSet();
        return saved.Concat(entries.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .Distinct().Where(e => !deleted.Contains(e)).ToList();
    }
}
