using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

/// <summary>
/// Backfill one-shot: preenche o <c>DeliveryLocationName</c> das liberações de entrega de
/// venda que ficaram em branco (criadas quando o nome era resolvido pelo armazém em vez do
/// cadastro de clientes). Resolve o nome pelo <see cref="IBusinessPartnerService"/> — que
/// funciona tanto no modo SAPB1 quanto standalone —, por isso roda em runtime e não como
/// migration SQL (a tabela local de parceiros fica vazia no modo SAP).
/// </summary>
public class SalesShipmentReleasesBackfillDeliveryLocationNameService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService)
{
    public async Task<SalesShipmentReleaseBackfillResultDto> ExecuteAsync()
    {
        var pending = await context.SalesShipmentReleases
            .Where(x => x.DeliveryLocationName == null || x.DeliveryLocationName == "")
            .ToListAsync();

        // Cache por CardCode para não bater no cadastro (SAP) repetidamente.
        var resolved = new Dictionary<string, string?>();
        var updated = 0;

        foreach (var release in pending)
        {
            if (string.IsNullOrEmpty(release.DeliveryLocationCode))
                continue;

            if (!resolved.TryGetValue(release.DeliveryLocationCode, out var name))
            {
                name = (await businessPartnerService.GetByIdAsync(release.DeliveryLocationCode))?.CardName;
                resolved[release.DeliveryLocationCode] = name;
            }

            if (string.IsNullOrEmpty(name))
                continue;

            release.DeliveryLocationName = name;
            updated++;
        }

        if (updated > 0)
            await context.SaveChangesAsync();

        return new SalesShipmentReleaseBackfillResultDto { Scanned = pending.Count, Updated = updated };
    }
}
