using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsPriceFixationsUpdateService(
    AppDbContext context,
    SalesContractsFixedVolumeService fixedVolumeService,
    ILogger<SalesContractsPriceFixationsUpdateService> logger)
{
    public async Task<SalesContractPriceFixation?> ExecuteAsync(
        Guid associationKey, SalesContractPriceFixation associationEntity)
    {
        try
        {
            var existingEntity = await context.SalesContractsPriceFixations
                                     .Include(x => x.SalesContract)
                                     .FirstOrDefaultAsync(x => x.Key == associationKey)
                                 ?? throw new NotFoundException("Price Fixation not found");

            if (existingEntity.Status != PriceFixationStatus.InApproval)
                throw new ApplicationException(
                    $"Fixação {existingEntity.Status} é imutável. " +
                    "Para corrigir, estorne a fixação e crie uma nova.");

            var contract = existingEntity.SalesContract;

            // Preserva status e auditoria: o payload do cliente não pode promover a
            // fixação a Confirmed, contornando a aprovação da diretoria.
            var status = existingEntity.Status;
            var createdAt = existingEntity.CreatedAt;
            var createdBy = existingEntity.CreatedBy;

            context.Entry(existingEntity).CurrentValues.SetValues(associationEntity);

            existingEntity.Status = status;
            existingEntity.CreatedAt = createdAt;
            existingEntity.CreatedBy = createdBy;
            existingEntity.UpdatedAt = DateTime.Now;

            // Salva o volume alterado ANTES de recalcular: RecalculateAsync consulta o
            // banco e não enxerga mudanças apenas rastreadas em memória.
            await context.SaveChangesAsync();

            if (contract != null)
            {
                await fixedVolumeService.RecalculateAsync(contract);
                await context.SaveChangesAsync();
            }

            return existingEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    public async Task<SalesContractPriceFixation?> ExecuteAsync(
        Guid parentKey, Guid associationKey, SalesContractPriceFixation associationEntity)
    {
        if (!await context.SalesContracts.AnyAsync(x => x.Key == parentKey))
            throw new NotFoundException("Sales Contract not found");

        return await ExecuteAsync(associationKey, associationEntity);
    }
}
