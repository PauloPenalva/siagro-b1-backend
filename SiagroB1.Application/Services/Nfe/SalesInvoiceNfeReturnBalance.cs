using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Quanto de cada item da venda já está em devolução — Pendente ou Confirmada; Cancelada não conta.
/// Pendente conta de propósito: duas devoluções abertas da mesma venda somariam mais que o vendido.
/// </summary>
public static class SalesInvoiceNfeReturnBalance
{
    private const decimal Tolerance = 0.001m;
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Task<Dictionary<Guid, decimal>> ReturnedByOriginItemAsync(
        AppDbContext context, Guid originInvoiceKey, Guid? excludingReturnKey) =>
        context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.SalesInvoiceItemOriginKey != null
                        && i.SalesInvoice!.SalesInvoiceOriginKey == originInvoiceKey
                        && i.SalesInvoice.InvoiceType == SalesInvoiceType.Return
                        && i.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.SalesInvoiceKey != excludingReturnKey)
            .GroupBy(i => i.SalesInvoiceItemOriginKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    /// <summary>
    /// As <paramref name="lines"/> desta devolução, somadas às OUTRAS devoluções não canceladas da
    /// mesma venda, não passam do vendido por item.
    /// </summary>
    public static async Task EnsureWithinAsync(
        AppDbContext context, SalesInvoice returnInvoice, IEnumerable<SalesInvoiceItem> lines)
    {
        var originKey = returnInvoice.SalesInvoiceOriginKey
                        ?? throw new DefaultException("A devolução está sem a venda de origem.");
        var returned = await ReturnedByOriginItemAsync(context, originKey, returnInvoice.Key);
        var list = lines.ToList();
        var originItemKeys = list.Where(l => l.SalesInvoiceItemOriginKey != null)
            .Select(l => l.SalesInvoiceItemOriginKey!.Value).ToList();
        var sold = await context.SalesInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && originItemKeys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value, i => i.Quantity);

        foreach (var line in list)
        {
            var key = line.SalesInvoiceItemOriginKey
                      ?? throw new DefaultException($"O item {line.ItemCode} da devolução não aponta um item da venda.");
            var available = sold.GetValueOrDefault(key) - returned.GetValueOrDefault(key);

            if (line.Quantity - available > Tolerance)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a devolução ({line.Quantity.ToString("N3", PtBr)}) passa do saldo " +
                    $"devolvível da venda ({available.ToString("N3", PtBr)}).");
        }
    }
}
