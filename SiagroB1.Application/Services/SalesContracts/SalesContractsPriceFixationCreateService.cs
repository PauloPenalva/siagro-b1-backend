using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Criação de fixação de preço em contrato de venda a fixar (PAF), exposta como OData action
/// (<c>SalesContractsPriceFixationCreate</c>). Espelha
/// <c>PurchaseContractsPriceFixationCreateService</c>.
/// </summary>
public class SalesContractsPriceFixationCreateService(
    AppDbContext context,
    SalesContractsFixedVolumeService fixedVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    ILogger<SalesContractsPriceFixationCreateService> logger)
{
    public async Task<SalesContractPriceFixation> ExecuteAsync(
        Guid salesContractKey,
        SalesContractPriceFixation associationEntity,
        string createdBy)
    {
        try
        {
            var contract = await context.SalesContracts
                               .FirstOrDefaultAsync(x => x.Key == salesContractKey)
                           ?? throw new NotFoundException("Sales contract not found");

            if (contract.Type != ContractType.ToBeDetermined)
                throw new ApplicationException(
                    "Fixação manual só é permitida em contrato a fixar (PAF). " +
                    "Contrato de preço fixo tem a fixação gerada na criação.");

            if (contract.Status != ContractStatus.Approved)
                throw new ApplicationException(
                    "Contrato precisa estar aprovado para receber fixação de preço.");

            if (associationEntity.FixationVolume <= 0)
                throw new ApplicationException("Volume da fixação deve ser maior que zero.");

            await fixedVolumeService.RecalculateAsync(contract);

            if (contract.FixedVolume + associationEntity.FixationVolume > contract.TotalVolume)
                throw new ApplicationException(
                    $"Volume excede o saldo disponível para fixação. " +
                    $"Disponível: {contract.AvailableVolumeToPricing:N3}, " +
                    $"solicitado: {associationEntity.FixationVolume:N3}.");

            associationEntity.SalesContractKey = contract.Key;
            associationEntity.Status = PriceFixationStatus.InApproval;
            associationEntity.CreatedAt = DateTime.Now;
            associationEntity.CreatedBy = createdBy;

            await context.AddAsync(associationEntity);

            // Recalcula já contando a nova fixação; o RowVersion do contrato
            // faz a guarda contra fixações concorrentes.
            contract.FixedVolume += associationEntity.FixationVolume;

            changeLog.Register(
                contract.Key,
                ContractChangeLogFields.PriceFixation,
                null,
                ContractChangeLogFields.DescribePriceFixation(
                    associationEntity.FixationVolume, associationEntity.FixationPrice,
                    associationEntity.Status, contract.UnitOfMeasureCode),
                createdBy);
            notificationOutbox.RegisterPriceFixation(
                contract, associationEntity, NotificationEventType.PriceFixationCreated, createdBy);


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
