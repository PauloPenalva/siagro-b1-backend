using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Vínculo do CT-e com as NF-e que ele transporta (spec 2026-10-17). É informativo: não muda saldo, título nem tributo.
/// A chave de acesso é a identidade; o vínculo com a NF de venda ou com a entrada do sistema é só o que foi encontrado.
/// </summary>
public static class PurchaseInvoiceTransportedDocuments
{
    public const string NotPendingMessage = "Só é possível alterar os vínculos de um CT-e pendente.";

    /// <summary>
    /// Procura a NF de venda (status ≠ Cancelado) com a chave; se não achar, a entrada (outra que não o CT-e, não CT-e,
    /// status ≠ Cancelado). Preenche UMA das FKs ou nenhuma — recalcula do zero, então serve também para refazer.
    /// </summary>
    public static async Task ResolveAsync(AppDbContext ctx, PurchaseInvoiceTransportedDocument doc, Guid cteKey)
    {
        doc.SalesInvoiceKey = null;
        doc.TransportedPurchaseInvoiceKey = null;

        var sale = await ctx.SalesInvoices.AsNoTracking()
            .Where(x => x.ChaveNFe == doc.AccessKey && x.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(x => (Guid?) x.Key).FirstOrDefaultAsync();
        if (sale is not null)
        {
            doc.SalesInvoiceKey = sale;
            return;
        }

        doc.TransportedPurchaseInvoiceKey = await ctx.PurchaseInvoices.AsNoTracking()
            .Where(x => x.ChaveNFe == doc.AccessKey && x.Key != cteKey && x.TaxDocumentKind != TaxDocumentKind.Cte
                        && x.InvoiceStatus != InvoiceStatus.Cancelled)
            .Select(x => (Guid?) x.Key).FirstOrDefaultAsync();
    }

    /// <summary>
    /// CT-e com XML e sem vínculos: um vínculo por chave distinta de <c>infDoc</c>, cada um resolvido. Os vínculos entram
    /// na coleção do CT-e; quem grava é o SaveChanges do chamador (o CT-e ainda não foi adicionado ao contexto).
    /// </summary>
    public static async Task SeedFromXmlAsync(AppDbContext ctx, PurchaseInvoice cte)
    {
        if (cte.TransportedDocuments.Count > 0 || cte.XmlData is not { Length: > 0 } xml || !SupplierCteXmlReader.IsCte(xml))
            return;

        foreach (var key in SupplierCteXmlReader.Read(xml).TransportedKeys.Distinct())
        {
            var doc = new PurchaseInvoiceTransportedDocument
            {
                Key = Guid.NewGuid(), PurchaseInvoiceKey = cte.Key, AccessKey = key,
            };
            await ResolveAsync(ctx, doc, cte.Key);
            cte.TransportedDocuments.Add(doc);
        }
    }
}
