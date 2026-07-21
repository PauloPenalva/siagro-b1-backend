using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesApprovationService(IUnitOfWork db, ILogger<SalesShipmentReleasesApprovationService> logger)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var sr = await db.Context.SalesShipmentReleases
                     .Include(x => x.SalesContract)
                     .ThenInclude(p => p!.SalesShipmentReleases)
                     .Include(x => x.SalesContract)
                     .ThenInclude(p => p!.SalesInvoiceItems).ThenInclude(i => i.SalesInvoice)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Sales Shipment Release not found key {key}");

        if (sr.Status is ReleaseStatus.Actived or ReleaseStatus.Cancelled or ReleaseStatus.Completed)
        {
            throw new ArgumentException("Sales Shipment Release not pending ou paused.");
        }

        if (sr.SalesContract == null)
        {
            throw new NotFoundException($"Sales Contract not found key {sr.SalesContractKey}");
        }

        if (sr.SalesContract.Status != ContractStatus.Approved)
        {
            throw new ArgumentException("Sales Contract not approved.");
        }

        // se a quantidade liberada é superior ao total disponível do contrato, lança a exceção
        if (sr.Status != ReleaseStatus.Paused &&
            sr.ReleasedQuantity > sr.SalesContract.TotalAvailableToReleaseWithoutProvisioning)
        {
            throw new ArgumentException("Quantity is higher than the total available to release.");
        }

        // Saldo FÍSICO (sem provisionamento): disponível por nota menos as reservas de
        // liberações já ATIVAS/pausadas (exclui a própria e outras pendentes). Bloqueia
        // aprovar liberação que o contrato não tem mais saldo físico para embarcar —
        // ex.: contrato já 100% faturado pelo fluxo legado.
        if (sr.Status != ReleaseStatus.Paused)
        {
            var reservedByActive = sr.SalesContract.SalesShipmentReleases
                .Where(r => r.Key != sr.Key &&
                            r.Status is ReleaseStatus.Actived or ReleaseStatus.Paused)
                .Sum(r => r.AvailableQuantity);

            var physicalAvailable = decimal.Round(
                sr.SalesContract.AvaiableVolume - reservedByActive, 3, MidpointRounding.ToEven);

            if (sr.ReleasedQuantity > physicalAvailable)
                throw new ArgumentException(
                    $"Saldo físico do contrato insuficiente. Disponível: {physicalAvailable:N3}.");
        }

        try
        {
            sr.Status = ReleaseStatus.Actived;
            sr.ApprovedBy = userName;
            sr.ApprovedAt = DateTime.Now;

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}
