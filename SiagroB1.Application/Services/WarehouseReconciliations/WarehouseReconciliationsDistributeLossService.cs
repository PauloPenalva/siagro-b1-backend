using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

public record WarehouseReconciliationLossLine(Guid ShipmentReleaseKey, decimal Quantity);

/// <summary>
/// Grava quanto da perda cai em cada liberação (GAC-1164 §9.4). Substitui o conjunto inteiro e só
/// em Rascunho. Aqui valida só a forma (quantidade positiva, liberação única); as regras que dependem
/// do saldo — fechar com a diferença, liberação elegível, saldo de hoje, contrato encerrado — rodam no
/// envio e na aprovação, em <see cref="WarehouseReconciliationsGuardService.EnsureLossDistributionAsync"/>,
/// porque o saldo muda entre digitar e decidir.
/// </summary>
public class WarehouseReconciliationsDistributeLossService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, IReadOnlyList<WarehouseReconciliationLossLine> lines, string userName)
    {
        var r = await db.Context.WarehouseReconciliations
                    .Include(x => x.Releases)
                    .FirstOrDefaultAsync(x => x.Key == key)
                ?? throw new NotFoundException(resource["WAREHOUSE_RECONCILIATION_NOT_FOUND"].Value);

        if (r.Status != WarehouseReconciliationStatus.Draft)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_NOT_DRAFT"].Value);

        // Arredonda ANTES de validar positividade/duplicidade: uma quantidade que só existe na
        // casa decimal descartada (ex.: 0,0004) tem que ser recusada como zero, não aceita como
        // positiva e só depois zerada na gravação.
        var rounded = lines
            .Select(l => new WarehouseReconciliationLossLine(
                l.ShipmentReleaseKey, decimal.Round(l.Quantity, 3, MidpointRounding.ToEven)))
            .ToList();

        if (rounded.Any(l => l.Quantity <= decimal.Zero) ||
            rounded.Select(l => l.ShipmentReleaseKey).Distinct().Count() != rounded.Count)
            throw new ApplicationException(resource["WAREHOUSE_RECONCILIATION_DISTRIBUTION_INVALID"].Value);

        db.Context.WarehouseReconciliationReleases.RemoveRange(r.Releases);

        foreach (var line in rounded)
        {
            db.Context.WarehouseReconciliationReleases.Add(new WarehouseReconciliationRelease
            {
                WarehouseReconciliationKey = r.Key,
                ShipmentReleaseKey = line.ShipmentReleaseKey,
                Quantity = line.Quantity,
            });
        }

        r.UpdatedAt = DateTime.Now;
        r.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
