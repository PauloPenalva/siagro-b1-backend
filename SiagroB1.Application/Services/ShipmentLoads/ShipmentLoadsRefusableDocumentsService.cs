using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Documentos de saída da carga que ainda podem ser recusados, com o volume devolvível de cada
/// um. É a fonte do diálogo de recusa.
/// </summary>
/// <remarks>
/// <b>Só documentos NORMAIS e CONFIRMADOS.</b> A devolução exige origem confirmada
/// (<c>SalesInvoicesConfirmService.ValidateLineItemBalance</c> e as validações do retorno), e
/// uma nota de devolução obviamente não se devolve.
/// <para>
/// Documento com <c>RefusableQuantity</c> zerado — já inteiramente devolvido — fica de fora:
/// oferecê-lo no diálogo só produziria erro na confirmação.
/// </para>
/// </remarks>
public class ShipmentLoadsRefusableDocumentsService(IUnitOfWork db)
{
    private const decimal Tolerance = 0.001m;

    public async Task<IReadOnlyList<ShipmentLoadRefusableDocumentDto>> ExecuteAsync(Guid shipmentLoadKey)
    {
        var invoices = await db.Context.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.ShipmentLoadKey == shipmentLoadKey &&
                        x.InvoiceType == SalesInvoiceType.Normal &&
                        x.InvoiceStatus == InvoiceStatus.Confirmed)
            .OrderBy(x => x.RowId)
            .ToListAsync();

        if (invoices.Count == 0)
            return [];

        var itemKeys = invoices
            .SelectMany(i => i.Items)
            .Where(i => i.Key != null)
            .Select(i => (Guid?)i.Key!.Value)
            .ToList();

        // Devoluções vivas apontando os itens destes documentos, agrupadas pela origem. Mesmo
        // predicado de ValidateLineItemBalance — o diálogo tem de oferecer exatamente o que a
        // confirmação vai aceitar, senão o usuário digita um número que o servidor recusa.
        var returnedByOriginItem = await db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceItemOriginKey != null &&
                        itemKeys.Contains(x.SalesInvoiceItemOriginKey) &&
                        x.SalesInvoice!.InvoiceType == SalesInvoiceType.Return &&
                        x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled)
            .GroupBy(x => x.SalesInvoiceItemOriginKey!.Value)
            .Select(g => new { OriginItemKey = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.OriginItemKey, x => x.Quantity);

        var documents = new List<ShipmentLoadRefusableDocumentDto>();

        foreach (var invoice in invoices)
        {
            var quantity = invoice.Items.Sum(i => i.Quantity);

            var alreadyReturned = invoice.Items
                .Where(i => i.Key != null)
                .Sum(i => returnedByOriginItem.GetValueOrDefault(i.Key!.Value));

            var refusable = decimal.Round(quantity - alreadyReturned, 3, MidpointRounding.ToEven);

            if (refusable <= Tolerance)
                continue;

            var firstItem = invoice.Items.FirstOrDefault();

            documents.Add(new ShipmentLoadRefusableDocumentDto
            {
                SalesInvoiceKey = invoice.Key.ToString(),
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                CardCode = invoice.CardCode,
                CardName = invoice.CardName,
                DeliveryCardCode = invoice.DeliveryCardCode,
                DeliveryCardName = invoice.DeliveryCardName,
                ItemCode = firstItem?.ItemCode,
                ItemName = firstItem?.ItemName,
                UnitOfMeasureCode = firstItem?.UnitOfMeasureCode,
                Quantity = decimal.Round(quantity, 3, MidpointRounding.ToEven),
                AlreadyReturnedQuantity = decimal.Round(alreadyReturned, 3, MidpointRounding.ToEven),
                RefusableQuantity = refusable,
            });
        }

        return documents;
    }
}
