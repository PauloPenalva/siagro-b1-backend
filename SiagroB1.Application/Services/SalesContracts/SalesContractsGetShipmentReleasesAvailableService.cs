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
    /// saldo FÍSICO (TotalVolume − AllocatedVolume, persistido a partir do ledger de
    /// alocações) menos o reservado por liberações abertas. Só aparecem contratos
    /// aprovados que ainda têm o que embarcar — evita liberar em contrato já esgotado
    /// (inclusive faturado por fora de liberação, no fluxo legado). Inclui liberações e
    /// notas para os getters serializarem corretamente na coluna "Saldo a liberar".
    /// Mantenha em sincronia com o getter da entidade.
    /// </summary>
    public IQueryable<SalesContract> Query()
    {
        return db.Context.SalesContracts
            .Include(x => x.SalesShipmentReleases)
            .Include(x => x.SalesInvoiceItems).ThenInclude(i => i.SalesInvoice)
            .Where(p => p.Status == ContractStatus.Approved &&
                        (p.TotalVolume
                            - p.AllocatedVolume
                            - p.SalesShipmentReleases
                                .Where(r => r.Status == ReleaseStatus.Pending
                                            || r.Status == ReleaseStatus.Actived
                                            || r.Status == ReleaseStatus.Paused)
                                .Sum(r => r.ReleasedQuantity - r.ShippedQuantity)
                        ) > 0
                     );
    }
}
