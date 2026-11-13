using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Inclui um contrato PLANEJADO na carga (GAC-1185), escolhido pela LIBERAÇÃO: de embarque na
/// compra, de entrega na venda. O contrato gravado é o da própria liberação.
/// <para>
/// É vínculo informativo: não mexe em saldo, peso nem valor — o contrato efetivo continua sendo o
/// das notas da carga. Por isso a regra é só de coerência: liberação ativa, contrato aprovado e do
/// mesmo produto da carga, cada liberação uma vez por carga, e carga não cancelada (qualquer outro
/// status aceita, o planejamento muda até o fim). O saldo da liberação NÃO é validado.
/// </para>
/// </summary>
public class ShipmentLoadsPlannedContractAddService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsPlannedContractAddService> logger)
{
    public async Task ExecuteAsync(
        Guid loadKey, PlannedContractType type, Guid releaseKey,
        decimal? quantity, string? notes, string user)
    {
        try
        {
            if (quantity is <= 0)
                throw new ApplicationException("A quantidade planejada deve ser maior que zero.");

            var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == loadKey)
                       ?? throw new NotFoundException("Carga não encontrada.");

            if (load.Status == ShipmentLoadStatus.Cancelled)
                throw new ApplicationException($"A carga {load.Code} está cancelada.");

            var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

            if (text is { Length: > 200 })
                throw new ApplicationException("A observação deve ter no máximo 200 caracteres.");

            var release = type == PlannedContractType.Purchase
                ? await FindPurchaseReleaseAsync(releaseKey)
                : await FindSalesReleaseAsync(releaseKey);

            var label = $"{release.ContractCode} (item {release.RowId})";

            if (release.Status != ReleaseStatus.Actived)
                throw new ApplicationException($"A liberação {label} não está ativa.");

            if (release.ContractStatus != ContractStatus.Approved)
                throw new ApplicationException($"O contrato {release.ContractCode} não está aprovado.");

            if (!string.Equals(release.ItemCode, load.ItemCode, StringComparison.OrdinalIgnoreCase))
                throw new ApplicationException($"O contrato {release.ContractCode} é de outro produto.");

            var duplicateMessage = $"A liberação {label} já está planejada nesta carga.";

            if (type == PlannedContractType.Purchase)
            {
                if (await context.ShipmentLoadsPlannedPurchaseContracts
                        .AnyAsync(x => x.ShipmentLoadKey == loadKey && x.ShipmentReleaseKey == releaseKey))
                    throw new ApplicationException(duplicateMessage);

                await context.ShipmentLoadsPlannedPurchaseContracts.AddAsync(new ShipmentLoadPlannedPurchaseContract
                {
                    ShipmentLoadKey = loadKey,
                    ContractKey = release.ContractKey,
                    ShipmentReleaseKey = releaseKey,
                    PlannedQuantity = quantity,
                    Notes = text,
                    CreatedAt = DateTime.Now,
                    CreatedBy = user,
                });
            }
            else
            {
                if (await context.ShipmentLoadsPlannedSalesContracts
                        .AnyAsync(x => x.ShipmentLoadKey == loadKey && x.SalesShipmentReleaseKey == releaseKey))
                    throw new ApplicationException(duplicateMessage);

                await context.ShipmentLoadsPlannedSalesContracts.AddAsync(new ShipmentLoadPlannedSalesContract
                {
                    ShipmentLoadKey = loadKey,
                    ContractKey = release.ContractKey,
                    SalesShipmentReleaseKey = releaseKey,
                    PlannedQuantity = quantity,
                    Notes = text,
                    CreatedAt = DateTime.Now,
                    CreatedBy = user,
                });
            }

            changeLog.Register(
                loadKey,
                type == PlannedContractType.Purchase
                    ? ShipmentLoadChangeLogFields.PlannedPurchaseContract
                    : ShipmentLoadChangeLogFields.PlannedSalesContract,
                null,
                ShipmentLoadChangeLogFields.DescribePlannedContract(release.ContractCode, release.RowId, quantity),
                user);

            try
            {
                await context.SaveChangesAsync();
            }
            // Inclusão concorrente da mesma liberação: o índice único (carga, contrato, liberação)
            // barra a segunda.
            catch (DbUpdateException e) when (FiscalStock.FiscalStockPeriodsService.IsUniqueViolation(e))
            {
                throw new ApplicationException(duplicateMessage);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    private async Task<PlannedRelease> FindPurchaseReleaseAsync(Guid releaseKey) =>
        await context.ShipmentReleases
            .Where(x => x.Key == releaseKey)
            .Select(x => new PlannedRelease(
                x.RowId, x.Status, x.PurchaseContractKey,
                x.PurchaseContract!.Code, x.PurchaseContract.ItemCode, x.PurchaseContract.Status))
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Liberação de embarque não encontrada.");

    private async Task<PlannedRelease> FindSalesReleaseAsync(Guid releaseKey) =>
        await context.SalesShipmentReleases
            .Where(x => x.Key == releaseKey)
            .Select(x => new PlannedRelease(
                x.RowId, x.Status, x.SalesContractKey,
                x.SalesContract!.Code, x.SalesContract.ItemCode, x.SalesContract.Status))
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Liberação de entrega não encontrada.");

    /// <summary>O que a regra precisa da liberação e do contrato dela, igual para compra e venda.</summary>
    private sealed record PlannedRelease(
        int RowId, ReleaseStatus Status, Guid ContractKey,
        string? ContractCode, string ItemCode, ContractStatus? ContractStatus);
}
