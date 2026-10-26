using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Inclui um contrato PLANEJADO na carga (GAC-1185). É vínculo informativo: não mexe em saldo,
/// peso nem valor — o contrato efetivo continua sendo o das notas da carga. Por isso a regra é
/// só de coerência: contrato aprovado, do mesmo produto da carga, uma vez por carga, e carga
/// não cancelada (qualquer outro status aceita, o planejamento muda até o fim).
/// </summary>
public class ShipmentLoadsPlannedContractAddService(
    AppDbContext context,
    ShipmentLoadsChangeLogService changeLog,
    ILogger<ShipmentLoadsPlannedContractAddService> logger)
{
    public async Task ExecuteAsync(
        Guid loadKey, PlannedContractType type, Guid contractKey,
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

            string? code;

            if (type == PlannedContractType.Purchase)
            {
                var contract = await context.PurchaseContracts
                                   .Where(x => x.Key == contractKey)
                                   .Select(x => new { x.Code, x.ItemCode, x.Status })
                                   .FirstOrDefaultAsync()
                               ?? throw new NotFoundException("Contrato de compra não encontrado.");

                EnsureEligible(contract.Code, contract.Status, contract.ItemCode, load.ItemCode);

                if (await context.ShipmentLoadsPlannedPurchaseContracts
                        .AnyAsync(x => x.ShipmentLoadKey == loadKey && x.ContractKey == contractKey))
                    throw new ApplicationException($"O contrato {contract.Code} já está planejado nesta carga.");

                code = contract.Code;

                await context.ShipmentLoadsPlannedPurchaseContracts.AddAsync(new ShipmentLoadPlannedPurchaseContract
                {
                    ShipmentLoadKey = loadKey,
                    ContractKey = contractKey,
                    PlannedQuantity = quantity,
                    Notes = text,
                    CreatedAt = DateTime.Now,
                    CreatedBy = user,
                });
            }
            else
            {
                var contract = await context.SalesContracts
                                   .Where(x => x.Key == contractKey)
                                   .Select(x => new { x.Code, x.ItemCode, x.Status })
                                   .FirstOrDefaultAsync()
                               ?? throw new NotFoundException("Contrato de venda não encontrado.");

                EnsureEligible(contract.Code, contract.Status, contract.ItemCode, load.ItemCode);

                if (await context.ShipmentLoadsPlannedSalesContracts
                        .AnyAsync(x => x.ShipmentLoadKey == loadKey && x.ContractKey == contractKey))
                    throw new ApplicationException($"O contrato {contract.Code} já está planejado nesta carga.");

                code = contract.Code;

                await context.ShipmentLoadsPlannedSalesContracts.AddAsync(new ShipmentLoadPlannedSalesContract
                {
                    ShipmentLoadKey = loadKey,
                    ContractKey = contractKey,
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
                ShipmentLoadChangeLogFields.DescribePlannedContract(code, quantity),
                user);

            try
            {
                await context.SaveChangesAsync();
            }
            // Inclusão concorrente do mesmo contrato: o índice único (carga, contrato) barra a segunda.
            catch (DbUpdateException e) when (FiscalStock.FiscalStockPeriodsService.IsUniqueViolation(e))
            {
                throw new ApplicationException($"O contrato {code} já está planejado nesta carga.");
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }

    private static void EnsureEligible(string? code, ContractStatus? status, string contractItem, string loadItem)
    {
        if (status != ContractStatus.Approved)
            throw new ApplicationException($"O contrato {code} não está aprovado.");

        if (!string.Equals(contractItem, loadItem, StringComparison.OrdinalIgnoreCase))
            throw new ApplicationException($"O contrato {code} é de outro produto.");
    }
}
