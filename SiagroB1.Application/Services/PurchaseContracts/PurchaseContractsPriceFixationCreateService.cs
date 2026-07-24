using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Criação de fixação de preço em contrato a fixar (PAF), exposta como OData action
/// (<c>PurchaseContractsPriceFixationCreate</c>) para que o frontend a invoque pelo
/// ODataModel e a nova linha reflita na tela sem recarregar a rota.
/// </summary>
public class PurchaseContractsPriceFixationCreateService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ILogger<PurchaseContractsPriceFixationCreateService> logger)
{
    public async Task<PurchaseContractPriceFixation> ExecuteAsync(
        Guid purchaseContractKey,
        PurchaseContractPriceFixation associationEntity,
        string createdBy)
    {
        try
        {
            var contract = await context.PurchaseContracts
                               .FirstOrDefaultAsync(x => x.Key == purchaseContractKey)
                           ?? throw new NotFoundException("Purchase contract not found");

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

            associationEntity.PurchaseContractKey = contract.Key;
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
