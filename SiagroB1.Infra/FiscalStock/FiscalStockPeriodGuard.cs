using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.FiscalStock;

/// <summary>
/// Trava do mês fechado do estoque fiscal: nenhum lançamento que mexe no estoque fiscal pode cair
/// (entrar, sair ou ser desfeito) num mês fechado da filial. A data conferida é a data de entrada no
/// estoque: entrada = PostingDate ?? IssueDate; saída = InvoiceDate; saldo inicial = BalanceDate. Recusa toda
/// data anterior ao 1º dia aberto (o mês seguinte ao último fechado), não só a do mês fechado.
/// </summary>
public class FiscalStockPeriodGuard(IUnitOfWork db)
{
    public async Task<bool> IsClosedAsync(string branchCode, int year, int month, CancellationToken ct = default) =>
        await db.Context.FiscalStockPeriods.AnyAsync(p =>
            p.BranchCode == branchCode && p.Year == year && p.Month == month &&
            p.Status == FiscalStockPeriodStatus.Closed, ct);

    /// <summary>1º dia do mês seguinte ao último mês fechado da filial; nulo se nenhum fechado.</summary>
    public async Task<DateTime?> FirstOpenDateAsync(string branchCode, CancellationToken ct = default)
    {
        var last = await db.Context.FiscalStockPeriods
            .Where(p => p.BranchCode == branchCode && p.Status == FiscalStockPeriodStatus.Closed)
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.Year, p.Month })
            .FirstOrDefaultAsync(ct);
        return last == null ? null : new DateTime(last.Year, last.Month, 1).AddMonths(1);
    }

    public async Task EnsureOpenAsync(string branchCode, DateTime stockDate, CancellationToken ct = default)
    {
        // Tudo antes do 1º dia aberto está congelado — inclusive o mês nunca fechado anterior ao primeiro
        // fechado (o instantâneo do fechamento já é o saldo a partir dali).
        var firstOpen = await FirstOpenDateAsync(branchCode, ct);
        if (firstOpen == null || stockDate.Date >= firstOpen.Value)
            return;

        throw new DefaultException(string.Create(CultureInfo.InvariantCulture,
            $"O estoque fiscal de {stockDate:MM/yyyy} da filial {branchCode} está fechado. " +
            $"Use uma data de entrada no estoque a partir de {firstOpen:dd/MM/yyyy} ou peça a reabertura do mês."));
    }

    /// <summary>Data de entrada no estoque do documento de entrada.</summary>
    public static DateTime? StockDate(PurchaseInvoice invoice) => invoice.PostingDate ?? invoice.IssueDate;

    /// <summary>Confere o documento de entrada só se alguma linha movimenta o estoque fiscal.</summary>
    public async Task EnsureOpenAsync(PurchaseInvoice invoice, DateTime? stockDate, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(invoice.BranchCode) || stockDate == null) return;
        if (!await db.Context.PurchaseInvoicesItems.AnyAsync(
                l => l.PurchaseInvoiceKey == invoice.Key && l.MovesFiscalInventory, ct)) return;
        await EnsureOpenAsync(invoice.BranchCode, stockDate.Value, ct);
    }

    /// <summary>Confere o documento de saída (InvoiceDate) só se alguma linha movimenta o estoque fiscal.</summary>
    public async Task EnsureOpenAsync(SalesInvoice invoice, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(invoice.BranchCode) || invoice.InvoiceDate == null) return;
        if (!await db.Context.SalesInvoicesItems.AnyAsync(
                l => l.SalesInvoiceKey == invoice.Key && l.MovesFiscalInventory, ct)) return;
        await EnsureOpenAsync(invoice.BranchCode, invoice.InvoiceDate.Value, ct);
    }

    /// <summary>Confere filial + data quando as duas existem (documento sem filial/data não conta).</summary>
    public Task EnsureOpenIfDatedAsync(string? branchCode, DateTime? stockDate, CancellationToken ct = default) =>
        string.IsNullOrEmpty(branchCode) || stockDate == null
            ? Task.CompletedTask
            : EnsureOpenAsync(branchCode, stockDate.Value, ct);

    /// <summary>Alguma linha do documento de saída movimenta o estoque fiscal.</summary>
    public Task<bool> SalesMovesStockAsync(Guid? invoiceKey, CancellationToken ct = default) =>
        db.Context.SalesInvoicesItems.AnyAsync(l => l.SalesInvoiceKey == invoiceKey && l.MovesFiscalInventory, ct);

    /// <summary>Status que conta no estoque fiscal (o desfazer desse documento mexe no mês gravado).</summary>
    public static bool IsBookable(InvoiceStatus? status) =>
        status is InvoiceStatus.Confirmed or InvoiceStatus.Returned;
}
