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
    SalesContractsChangeLogService changeLog,
    ILogger<SalesContractsDeliveryLocationsCreateService> logger)
{
    public async Task<SalesContractDeliveryLocation> ExecuteAsync(
        Guid salesContractKey, SalesContractDeliveryLocation associationEntity, string userName)
    {
        try
        {
            var contract = await context.SalesContracts.FindAsync(salesContractKey)
                ?? throw new NotFoundException("Sales contract not found");

            // Local de entrega é editável mesmo depois de aprovado — é o que permite ao
            // contrato já faturado cadastrar o destino exigido pela liberação de entrega.
            SalesContractsPostApprovalGuard.EnsureEditable(contract);

            var duplicate = await context.SalesContractsDeliveryLocations
                .AnyAsync(x => x.SalesContractKey == salesContractKey
                               && x.CardCode == associationEntity.CardCode);
            if (duplicate)
                throw new DefaultException("Este local de entrega ja foi informado no contrato.");

            associationEntity.SalesContract = contract;
            associationEntity.CardName =
                (await businessPartnerService.GetByIdAsync(associationEntity.CardCode))?.CardName;

            await context.AddAsync(associationEntity);

            changeLog.Register(
                salesContractKey,
                ContractChangeLogFields.DeliveryLocation,
                null,
                Describe(associationEntity),
                userName);

            await context.SaveChangesAsync();

            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    /// <summary>
    /// Como o local aparece no log: código e nome juntos, para a linha continuar legível mesmo
    /// depois de o parceiro ser renomeado ou o local removido.
    /// </summary>
    internal static string Describe(SalesContractDeliveryLocation location) =>
        string.IsNullOrWhiteSpace(location.CardName)
            ? location.CardCode
            : $"{location.CardCode} - {location.CardName}";
}
