using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Quanto de cada item da entrada já está em devolução — Pendente ou Confirmada; Cancelada não conta.
/// Pendente conta de propósito: duas devoluções abertas da mesma entrada somariam mais que o comprado.
/// </summary>
public static class PurchaseInvoiceNfeReturnBalance
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static Task<Dictionary<Guid, decimal>> ReturnedByOriginItemAsync(
        AppDbContext context, Guid originInvoiceKey, Guid? excludingReturnKey) =>
        context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.PurchaseInvoiceItemOriginKey != null
                        && i.PurchaseInvoice!.PurchaseInvoiceOriginKey == originInvoiceKey
                        && i.PurchaseInvoice.InvoiceType == PurchaseInvoiceType.Return
                        && i.PurchaseInvoice.IsNfeReturn
                        && i.PurchaseInvoice.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.PurchaseInvoiceKey != excludingReturnKey)
            .GroupBy(i => i.PurchaseInvoiceItemOriginKey!.Value)
            .Select(g => new { g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Quantity);

    /// <summary>
    /// As <paramref name="lines"/> desta devolução, somadas às OUTRAS devoluções não canceladas da
    /// mesma entrada, não passam do comprado por item.
    /// </summary>
    public static async Task EnsureWithinAsync(
        AppDbContext context, PurchaseInvoice returnInvoice, IEnumerable<PurchaseInvoiceItem> lines)
    {
        var originKey = returnInvoice.PurchaseInvoiceOriginKey
                        ?? throw new DefaultException("A devolução está sem a entrada de origem.");
        var returned = await ReturnedByOriginItemAsync(context, originKey, returnInvoice.Key);
        var list = lines.ToList();
        var originItemKeys = list.Where(l => l.PurchaseInvoiceItemOriginKey != null)
            .Select(l => l.PurchaseInvoiceItemOriginKey!.Value).ToList();
        var bought = await context.PurchaseInvoicesItems.AsNoTracking()
            .Where(i => i.Key != null && originItemKeys.Contains(i.Key.Value))
            .ToDictionaryAsync(i => i.Key!.Value, i => i.Quantity);

        foreach (var line in list)
        {
            var key = line.PurchaseInvoiceItemOriginKey
                      ?? throw new DefaultException($"O item {line.ItemCode} da devolução não aponta um item da entrada.");
            var available = bought.GetValueOrDefault(key) - returned.GetValueOrDefault(key);

            if (line.Quantity > available)
                throw new DefaultException(
                    $"Item {line.ItemCode}: a devolução ({line.Quantity.ToString("N3", PtBr)}) passa do saldo " +
                    $"devolvível da compra ({available.ToString("N3", PtBr)}).");
        }
    }
}
