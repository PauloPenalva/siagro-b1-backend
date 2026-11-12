using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;
using SiagroB1.Application.Services.MasterData;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsUpdateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesContractsDeliveryLocationsUpdateService> logger)
{
    public Task<SalesContractDeliveryLocation?> ExecuteAsync(
        Guid associationKey, SalesContractDeliveryLocation associationEntity) =>
        UpdateAsync(associationKey, associationEntity);

    public async Task<SalesContractDeliveryLocation?> ExecuteAsync(
        Guid parentKey, Guid associationKey, SalesContractDeliveryLocation associationEntity)
    {
        if (!context.SalesContracts.Any(x => x.Key == parentKey))
            throw new NotFoundException("Sales contract not found");
        return await UpdateAsync(associationKey, associationEntity);
    }

    private async Task<SalesContractDeliveryLocation?> UpdateAsync(
        Guid associationKey, SalesContractDeliveryLocation associationEntity)
    {
        try
        {
            var existingEntity = await context.SalesContractsDeliveryLocations.FindAsync(associationKey)
                ?? throw new NotFoundException("Delivery location not found");

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);
            // Depois do SetValues e em `existingEntity`, senao a gravacao se perde.
            existingEntity.CardName =
                InactiveRecordGuard.Partner(
                    await businessPartnerService.GetByIdAsync(associationEntity.CardCode),
                    InactiveRecordGuard.Original<string>(context.Entry(existingEntity), nameof(SalesContractDeliveryLocation.CardCode)),
                    "O local de entrega")?.CardName;

            await context.SaveChangesAsync();
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}
