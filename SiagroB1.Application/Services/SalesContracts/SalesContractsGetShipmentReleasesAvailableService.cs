using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsGetShipmentReleasesAvailableService(
    IUnitOfWork db,
    ILogger<SalesContractsGetShipmentReleasesAvailableService> logger)
{
    /// <summary>
    /// Espelho em SQL de <see cref="SalesContract.PhysicalAvailableToRelease"/>
    /// (EF não traduz as [NotMapped] <c>AvaiableVolume</c>/<c>AvailableQuantity</c>):
    /// saldo FÍSICO por nota menos o reservado por liberações abertas. Só aparecem
    /// contratos aprovados que ainda têm o que embarcar — evita liberar em contrato já
    /// esgotado (inclusive faturado por fora de liberação, no fluxo legado). Inclui as
    /// notas para o getter físico serializar corretamente na coluna "Saldo a liberar".
    /// Mantenha em sincronia com o getter da entidade.
    /// </summary>
    public IQueryable<SalesContract> Query()
    {
        return db.Context.SalesContracts
            .Include(x => x.SalesShipmentReleases)
            .Include(x => x.SalesInvoiceItems).ThenInclude(i => i.SalesInvoice)
            .Where(p => p.Status == ContractStatus.Approved &&
                        (p.TotalVolume
                            - p.SalesInvoiceItems
                                .Where(i => (i.SalesInvoice!.InvoiceStatus == InvoiceStatus.Confirmed
                                             || i.SalesInvoice.InvoiceStatus == InvoiceStatus.Returned)
                                            && i.SalesInvoice.InvoiceType == SalesInvoiceType.Normal)
                                .Sum(i => i.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed
                                    ? i.DeliveredQuantity - i.QuantityLoss
                                    : i.Quantity)
                            + p.SalesInvoiceItems
                                .Where(i => i.SalesInvoice!.InvoiceStatus == InvoiceStatus.Confirmed
                                            && i.SalesInvoice.InvoiceType == SalesInvoiceType.Return)
                                .Sum(i => i.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed
                                    ? i.DeliveredQuantity - i.QuantityLoss
                                    : i.Quantity)
                            - p.SalesShipmentReleases
                                .Where(r => r.Status == ReleaseStatus.Pending
                                            || r.Status == ReleaseStatus.Actived
                                            || r.Status == ReleaseStatus.Paused)
                                .Sum(r => r.ReleasedQuantity - r.ShippedQuantity)
                        ) > 0
                     );
    }
}
