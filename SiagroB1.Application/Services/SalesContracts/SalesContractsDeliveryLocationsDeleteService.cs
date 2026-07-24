using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsDeleteService(
    AppDbContext context,
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsDeliveryLocationsDeleteService> logger)
{
    public Task<bool> ExecuteAsync(Guid associationKey, string userName) =>
        Delete(associationKey, userName);

    public async Task<bool> ExecuteAsync(Guid parentKey, Guid associationKey, string userName)
    {
        if (!context.SalesContracts.Any(x => x.Key == parentKey))
            throw new NotFoundException("Sales contract not found");
        return await Delete(associationKey, userName);
    }

    private async Task<bool> Delete(Guid associationKey, string userName)
    {
        try
        {
            // Include do contrato: a remoção precisa do status para a guarda, e a rota curta
            // (sem parentKey) não tem outro caminho até ele.
            var existingEntity = await context.SalesContractsDeliveryLocations
                    .Include(x => x.SalesContract)
                    .FirstOrDefaultAsync(x => x.Key == associationKey)
                ?? throw new NotFoundException("Delivery location not found");

            if (existingEntity.SalesContract is not null)
            {
                SalesContractsPostApprovalGuard.EnsureEditable(existingEntity.SalesContract);
            }

            context.SalesContractsDeliveryLocations.Remove(existingEntity);

            if (existingEntity.SalesContractKey is { } contractKey)
            {
                changeLog.Register(
                    contractKey,
                    ContractChangeLogFields.DeliveryLocation,
                    SalesContractsDeliveryLocationsCreateService.Describe(existingEntity),
                    null,
                    userName);
            }

            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
}
