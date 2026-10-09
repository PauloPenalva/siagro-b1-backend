using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>Devoluções da recusa aguardando NF-e da carga — alimenta o painel do detalhe da carga.</summary>
public class ShipmentLoadsPendingRefusalService(IUnitOfWork db)
{
    public async Task<IReadOnlyList<ShipmentLoadPendingRefusalReturnDto>> ExecuteAsync(Guid shipmentLoadKey)
    {
        var refusal = await db.Context.ShipmentLoadRefusals.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShipmentLoadKey == shipmentLoadKey && x.Status == ShipmentLoadRefusalStatus.Pending);

        if (refusal is null)
            return [];

        var returns = await db.Context.SalesInvoices.AsNoTracking()
            .Include(i => i.Items)
            .Where(i => i.ShipmentLoadRefusalKey == refusal.Key)
            .OrderBy(i => i.InvoiceNumber)
            .ToListAsync();

        return returns.Select(i => new ShipmentLoadPendingRefusalReturnDto
        {
            RefusalKey = refusal.Key!.Value.ToString(),
            Destination = refusal.Destination.ToString(),
            DestinationWarehouseCode = refusal.DestinationWarehouseCode,
            DestinationWarehouseName = refusal.DestinationWarehouseName,
            Reason = refusal.Reason,
            SalesInvoiceKey = i.Key.ToString(),
            InvoiceNumber = i.InvoiceNumber,
            CardName = i.CardName,
            Quantity = i.Items.Sum(x => x.Quantity),
            InvoiceStatus = i.InvoiceStatus.ToString(),
            NfeStatus = i.NfeStatus.ToString(),
            TaxDocumentNumber = i.TaxDocumentNumber,
            TaxDocumentSeries = i.TaxDocumentSeries,
            NfeConfirmationError = i.NfeConfirmationError,
        }).ToList();
    }
}
