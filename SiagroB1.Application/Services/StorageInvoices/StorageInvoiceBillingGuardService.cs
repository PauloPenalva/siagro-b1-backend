using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageInvoices;

/// <summary>
/// Trava do faturamento da fatura de armazenagem (spec 2026-10-19 parte 3): Σ dos documentos de saída vinculados e não
/// cancelados — rascunho incluído, para ninguém faturar em dobro — nunca passa do total da fatura. Lê o rastreador:
/// chame com as mudanças do documento já aplicadas e ANTES do SaveChanges.
/// </summary>
public static class StorageInvoiceBillingGuardService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Total − Σ GrandTotal dos documentos vinculados não cancelados (gravados), menos o ignorado.</summary>
    public static async Task<decimal> GetBalanceAsync(AppDbContext context, Guid storageInvoiceKey, Guid? ignoringSalesInvoiceKey = null)
    {
        var total = await context.StorageInvoices.AsNoTracking()
            .Where(i => i.Key == storageInvoiceKey).Select(i => i.TotalAmount).SingleAsync();
        var lines = await context.SalesInvoicesItems.AsNoTracking()
            .Where(l => l.SalesInvoice!.StorageInvoiceKey == storageInvoiceKey &&
                        l.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled &&
                        l.SalesInvoiceKey != ignoringSalesInvoiceKey)
            .ToListAsync();
        return total - lines.Sum(l => l.GrandTotal);
    }

    /// <summary>Pela chave do documento (caminhos de linha). Documento sem fatura vinculada: no-op.</summary>
    public static async Task EnsureCanBillAsync(AppDbContext context, Guid? salesInvoiceKey)
    {
        if (salesInvoiceKey is not { } key) return;
        if (await context.SalesInvoices.FindAsync(key) is { StorageInvoiceKey: not null } invoice)
            await EnsureCanBillAsync(context, invoice);
    }

    public static async Task EnsureCanBillAsync(AppDbContext context, SalesInvoice invoice)
    {
        if (invoice.StorageInvoiceKey is not { } key || invoice.InvoiceStatus == InvoiceStatus.Cancelled) return;

        var storage = await context.StorageInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key)
                      ?? throw new NotFoundException("Fatura de armazenagem não encontrada.");
        // Situação só barra documento NOVO: editar um documento já vinculado de fatura paga não deve travar.
        var isNew = context.Entry(invoice).State is EntityState.Added or EntityState.Detached;
        if (isNew && storage.Status is StorageInvoiceStatus.Cancelled or StorageInvoiceStatus.Paid)
            throw new DefaultException($"A fatura {storage.Code} está {(storage.Status == StorageInvoiceStatus.Paid ? "paga" : "cancelada")}: não pode ser faturada.");
        if (!string.Equals(storage.CardCode, invoice.CardCode, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException($"O documento de saída precisa ser do mesmo parceiro da fatura {storage.Code}.");

        var balance = await GetBalanceAsync(context, key, invoice.Key);
        if (await DocumentTotalAsync(context, invoice) > balance)
            throw new DefaultException(
                $"Valor excede o saldo a faturar da fatura {storage.Code} (saldo R$ {balance.ToString("N2", PtBr)}).");
    }

    /// <summary>Σ GrandTotal das linhas do documento: gravadas (com o valor em memória, se rastreadas) + Added − Deleted.</summary>
    private static async Task<decimal> DocumentTotalAsync(AppDbContext context, SalesInvoice invoice)
    {
        var saved = await context.SalesInvoicesItems.Where(l => l.SalesInvoiceKey == invoice.Key).ToListAsync();
        var entries = context.ChangeTracker.Entries<SalesInvoiceItem>()
            .Where(e => e.Entity.SalesInvoiceKey == invoice.Key).ToList();
        var deleted = entries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity).ToHashSet();
        return saved
            .Concat(entries.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .Concat(invoice.Items)
            .Distinct()
            .Where(l => !deleted.Contains(l))
            .Sum(l => l.GrandTotal);
    }
}
