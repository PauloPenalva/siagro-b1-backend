using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsDeliveryLocationsCreateService(
    AppDbContext context,
    IBusinessPartnerService businessPartnerService,
    ILogger<SalesContractsDeliveryLocationsCreateService> logger)
{
    public async Task<SalesContractDeliveryLocation> ExecuteAsync(
        Guid salesContractKey, SalesContractDeliveryLocation associationEntity)
    {
        try
        {
            var contract = await context.SalesContracts.FindAsync(salesContractKey)
                ?? throw new NotFoundException("Sales contract not found");

            var duplicate = await context.SalesContractsDeliveryLocations
                .AnyAsync(x => x.SalesContractKey == salesContractKey
                               && x.CardCode == associationEntity.CardCode);
            if (duplicate)
                throw new DefaultException("Este local de entrega ja foi informado no contrato.");

            associationEntity.SalesContract = contract;
            associationEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

            await context.AddAsync(associationEntity);
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
