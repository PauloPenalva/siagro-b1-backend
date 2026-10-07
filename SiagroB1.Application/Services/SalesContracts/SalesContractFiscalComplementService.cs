using Microsoft.EntityFrameworkCore;

using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public record SalesContractFiscalComplementInput(
    int? UsageCode, int? PaymentConditionCode, string? AdditionalInfo, string? CustomerOrderNumber, string? CustomerOrderItem);

/// <summary>Complemento fiscal do contrato de venda (spec 2026-10-07 §4.1). Só quem tem SALES_CONTRACT_FISCAL_EDIT grava.</summary>
public class SalesContractFiscalComplementService(IUnitOfWork db, IUsage usageService, IUserPermissions permissions)
{
    public async Task<SalesContractFiscalComplementDto?> GetAsync(Guid salesContractKey)
    {
        var entity = await db.Context.SalesContractFiscalComplements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.SalesContractKey == salesContractKey);
        if (entity is null) return null;

        // Nomes só para exibição. A natureza pelo IUsage (dual-mode: USAGES ou OUSG); a condição é tabela local.
        var usageName = entity.UsageCode is { } usageCode ? (await usageService.GetByIdAsync(usageCode))?.Name : null;
        var conditionName = entity.PaymentConditionCode is { } conditionCode
            ? await db.Context.PaymentConditions.AsNoTracking().Where(x => x.Code == conditionCode).Select(x => x.Name).FirstOrDefaultAsync()
            : null;

        return new SalesContractFiscalComplementDto
        {
            SalesContractKey = entity.SalesContractKey,
            UsageCode = entity.UsageCode,
            UsageName = usageName,
            PaymentConditionCode = entity.PaymentConditionCode,
            PaymentConditionName = conditionName,
            AdditionalInfo = entity.AdditionalInfo,
            CustomerOrderNumber = entity.CustomerOrderNumber,
            CustomerOrderItem = entity.CustomerOrderItem,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy,
            IsComplete = entity.UsageCode.HasValue && entity.PaymentConditionCode.HasValue,
        };
    }

    public async Task<SalesContractFiscalComplementDto> SetAsync(Guid salesContractKey, SalesContractFiscalComplementInput input, string userName)
    {
        if (!await permissions.HasAsync(userName, PermissionCodes.SalesContractFiscalEdit))
            throw new DefaultException("Você não tem permissão para alterar o complemento fiscal do contrato.");

        if (!await db.Context.SalesContracts.AnyAsync(x => x.Key == salesContractKey))
            throw new NotFoundException($"Sales contract not found key {salesContractKey}");

        if (input.UsageCode is { } usageCode)
        {
            var usage = await usageService.GetByIdAsync(usageCode);
            if (usage is null || usage.Inactive || usage.Direction != UsageDirection.Outgoing)
                throw new DefaultException($"A natureza de operação {usageCode} não é uma natureza de saída ativa.");
        }

        if (input.PaymentConditionCode is { } conditionCode &&
            !await db.Context.PaymentConditions.AnyAsync(x => x.Code == conditionCode))
            throw new DefaultException($"Condição de pagamento {conditionCode} não encontrada.");

        var orderNumber = CustomerOrderRules.NormalizeNumber(input.CustomerOrderNumber);
        var orderItem = CustomerOrderRules.NormalizeItem(input.CustomerOrderItem);

        var entity = await db.Context.SalesContractFiscalComplements.FirstOrDefaultAsync(x => x.SalesContractKey == salesContractKey);
        if (entity is null)
        {
            entity = new SalesContractFiscalComplement { SalesContractKey = salesContractKey };
            await db.Context.SalesContractFiscalComplements.AddAsync(entity);
        }

        entity.UsageCode = input.UsageCode;
        entity.PaymentConditionCode = input.PaymentConditionCode;
        entity.AdditionalInfo = Blank(input.AdditionalInfo);
        entity.CustomerOrderNumber = orderNumber;
        entity.CustomerOrderItem = orderItem;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;

        await db.SaveChangesAsync();
        return (await GetAsync(salesContractKey))!;
    }

    // String vazia do diálogo = não informado (mesma regra do ItemComplementService).
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
