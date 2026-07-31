using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesCancelationService(
    AppDbContext context,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    ILogger<SalesShipmentReleasesCancelationService> logger)
{
    /// <summary>
    /// Cancela a liberação de entrega de venda, inclusive quando já houve faturamento
    /// (cenário de troca de armazém). Apenas o saldo NÃO romaneado/faturado volta para o
    /// contrato de origem — <see cref="SalesShipmentRelease.ConsumedQuantity"/> congela o
    /// que já foi faturado em <see cref="SalesShipmentRelease.ShippedQuantity"/>.
    /// </summary>
    public async Task ExecuteAsync(Guid key, string userName, string cancellationReason)
    {
        if (string.IsNullOrWhiteSpace(cancellationReason))
            throw new ApplicationException("Informe o motivo do cancelamento.");

        var sr = await context.SalesShipmentReleases
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Sales Shipment Release not found key {key}");

        var contract = await context.SalesContracts
            .FirstOrDefaultAsync(x => x.Key == sr.SalesContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível cancelar a liberação de entrega.");

        if (sr.Status is ReleaseStatus.Cancelled or ReleaseStatus.Completed)
            throw new ApplicationException("Liberação já cancelada ou finalizada: não é possível cancelar.");

        // Calcula sem gravar: uma tentativa recusada não pode deixar efeito no banco.
        var shipped = await recalcShipped.CalculateShippedAsync(sr.Key);

        var balance = SalesShipmentRelease.CalculateAvailableQuantity(sr.ReleasedQuantity, shipped);

        // Faturada ALÉM do liberado: nem cancelar nem finalizar resolvem — o excedente precisa
        // ser regularizado primeiro. Não mandar finalizar aqui, que o guard do
        // SalesShipmentReleasesCloseService também recusa o negativo.
        if (balance < decimal.Zero)
            throw new ApplicationException(
                $"Liberação faturada além do volume liberado. Liberado: {sr.ReleasedQuantity:N3}, " +
                $"faturado: {shipped:N3}, saldo: {balance:N3}. " +
                "Regularize o excedente antes de cancelar a liberação.");

        // Sem saldo a devolver ao contrato, cancelar não teria efeito algum:
        // a liberação já cumpriu o que foi liberado e deve ser finalizada.
        if (balance == decimal.Zero)
            throw new ApplicationException(
                "Liberação de entrega sem saldo disponível: não é possível cancelar. Utilize a ação Finalizar.");

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
            "Liberação de entrega {Key} cancelada por {User}. Consumido do contrato: {Consumed}. Motivo: {Reason}",
            sr.Key, userName, sr.ConsumedQuantity, sr.CancellationReason);
    }
}
