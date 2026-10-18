using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Regras da emissão do complemento de ICMS de saída e da trava na nota original (spec 2026-10-18 S7 e S9), comuns à
/// prontidão da NF-e, à revalidação antes da reserva do número e ao cancelamento da original.
/// </summary>
public static class SalesInvoiceIcmsComplement
{
    /// <summary>V3.</summary>
    public static string MissingIcmsMessage(string? itemCode) =>
        $"Item {itemCode}: informe a base e o valor do ICMS do complemento.";

    /// <summary>V4.</summary>
    public static string IcmsAboveBaseMessage(string? itemCode) =>
        $"Item {itemCode}: o ICMS do complemento não pode ser maior que a base.";

    /// <summary>V5.</summary>
    public static string NoLongerAuthorizedMessage(string? originalNumber) =>
        $"A nota original {originalNumber} não está mais autorizada: o complemento não pode ser emitido.";

    /// <summary>V6.</summary>
    public static string ActiveComplementMessage(string? originalNumber, string? complementNumber) =>
        $"A nota {originalNumber} tem o complemento de ICMS {complementNumber} ativo: cancele o complemento antes.";

    /// <summary>
    /// A regra das linhas e da original, antes de reservar número: a original é recarregada (pode ter sido cancelada
    /// entre a criação e a transmissão, V5) e toda linha leva base e ICMS, com ICMS ≤ base (V3/V4) — a linha gravada por
    /// fora do PATCH não passou pela regra da criação. Devolve a chave da NF-e original, a do NFref.
    /// </summary>
    public static async Task<string> EnsureIssuableAsync(AppDbContext context, SalesInvoice complement)
    {
        var lines = NfeItemNumbering.Ordered(complement.Items);
        var keys = lines.Where(l => l.ComplementedItemKey is not null).Select(l => l.ComplementedItemKey!.Value).Distinct().ToList();
        var originals = await context.SalesInvoicesItems.AsNoTracking()
            .Include(i => i.SalesInvoice)
            .Where(i => keys.Contains(i.Key!.Value))
            .ToDictionaryAsync(i => i.Key!.Value);

        SalesInvoice? origin = null;
        foreach (var line in lines)
        {
            if (line.ComplementedItemKey is not { } key || !originals.TryGetValue(key, out var original) ||
                original.SalesInvoice is not { } lineOrigin)
                throw new DefaultException($"Item {line.ItemCode}: o complemento de ICMS exige o item da nota original.");

            // S2: um complemento referencia UMA nota original.
            if (origin is not null && lineOrigin.Key != origin.Key)
                throw new DefaultException("O complemento de ICMS referencia uma nota original só.");

            origin = lineOrigin;
        }

        if (origin is null)
            throw new DefaultException("O documento não tem itens.");

        if (!IsAuthorizedOrigin(origin))
            throw new DefaultException(NoLongerAuthorizedMessage(origin.InvoiceNumber));

        foreach (var line in lines)
        {
            if (line.IcmsBase <= 0m || line.IcmsValue <= 0m)
                throw new DefaultException(MissingIcmsMessage(line.ItemCode));

            if (line.IcmsValue > line.IcmsBase)
                throw new DefaultException(IcmsAboveBaseMessage(line.ItemCode));
        }

        return origin.ChaveNFe!;
    }

    /// <summary>
    /// S9 (C8 da entrada): a original não é cancelada — documento nem NF-e — enquanto houver linha de complemento de ICMS
    /// apontando um item dela num documento não cancelado; o NFref do complemento ficaria apontando uma NF-e cancelada.
    /// </summary>
    public static async Task EnsureNoActiveComplementAsync(AppDbContext context, SalesInvoice original)
    {
        var complement = await context.SalesInvoicesItems.AsNoTracking()
            .Where(l => l.ComplementedItemKey != null &&
                        l.ComplementedItem!.SalesInvoiceKey == original.Key &&
                        l.SalesInvoice!.InvoiceType == SalesInvoiceType.IcmsComplement &&
                        l.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(l => new { l.SalesInvoice!.InvoiceNumber })
            .OrderBy(x => x.InvoiceNumber)
            .FirstOrDefaultAsync();

        if (complement is not null)
            throw new DefaultException(ActiveComplementMessage(original.InvoiceNumber, complement.InvoiceNumber));
    }

    /// <summary>A venda Normal com NF-e autorizada e não cancelada (devolvida em parte continua complementável).</summary>
    private static bool IsAuthorizedOrigin(SalesInvoice origin) =>
        origin.InvoiceType == SalesInvoiceType.Normal &&
        origin.NfeStatus == NfeStatus.Authorized &&
        origin.InvoiceStatus != InvoiceStatus.Cancelled &&
        origin.ChaveNFe is { Length: 44 };
}
