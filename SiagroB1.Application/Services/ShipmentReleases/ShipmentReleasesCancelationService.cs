using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesCancelationService(
    AppDbContext context,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ILogger<ShipmentReleasesCancelationService> logger)
{
    /// <summary>
    /// Cancela a liberação de embarque, inclusive quando já houve movimentação de venda
    /// (cenário de troca de armazém: cancela-se a liberação atual e cria-se outra no
    /// novo armazém). Apenas o saldo NÃO romaneado volta para o contrato de origem —
    /// <see cref="Domain.Entities.ShipmentRelease.ConsumedQuantity"/> congela o que já
    /// foi embarcado em <see cref="Domain.Entities.ShipmentRelease.ShippedQuantity"/>.
    /// </summary>
    /// <remarks>
    /// Romaneios vinculados não bloqueiam o cancelamento. Purchase/PurchaseReturn
    /// consomem o eixo de ALOCAÇÃO do contrato (<c>AllocatedVolume</c>, via
    /// <c>PurchaseContractAllocation</c>), independente do eixo de liberação — as
    /// alocações permanecem intactas após o cancelamento.
    /// </remarks>
    public async Task ExecuteAsync(Guid key, string userName, string cancellationReason)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var sr = await context.ShipmentReleases
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");

        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == sr.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível cancelar a liberação de embarque.");

        if (sr.Status is ReleaseStatus.Cancelled or ReleaseStatus.Completed)
            throw new ApplicationException("Liberação já cancelada ou finalizada: não é possível cancelar.");

        // Calcula sem gravar: uma tentativa recusada não pode deixar efeito no banco.
        var shipped = await recalcShipped.CalculateShippedAsync(sr.Key);

        // Sem saldo a devolver ao contrato, cancelar não teria efeito algum:
        // a liberação já cumpriu o que foi liberado e deve ser finalizada.
        if (ShipmentRelease.CalculateAvailableQuantity(sr.ReleasedQuantity, shipped) <= decimal.Zero)
            throw new ApplicationException(
                "Liberação de embarque sem saldo disponível: não é possível cancelar. Utilize a ação Finalizar.");

        // Congela ShippedQuantity junto com a virada de status, num único SaveChanges.
        sr.ShippedQuantity = shipped;
        sr.Status = ReleaseStatus.Cancelled;
        sr.CancellationReason = cancellationReason.Trim();
        sr.CanceledAt = DateTime.Now;
        sr.CanceledBy = userName;
        sr.UpdatedAt = DateTime.Now;
        sr.UpdatedBy = userName;

        await context.SaveChangesAsync();

        logger.LogInformation(
            "Liberação de embarque {Key} cancelada por {User}. Consumido do contrato: {Consumed}. Motivo: {Reason}",
            sr.Key, userName, sr.ConsumedQuantity, sr.CancellationReason);
    }
}
