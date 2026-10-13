using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Complemento fiscal do contrato de compra (spec 2026-10-13). Só STANDALONE; só quem tem
/// PURCHASE_CONTRACT_FISCAL_EDIT grava. Toda troca efetiva de natureza vai para o log do contrato, no mesmo
/// SaveChanges.
/// </summary>
public class PurchaseContractFiscalComplementService(
    IUnitOfWork db,
    IUsage usageService,
    IUserPermissions permissions,
    PurchaseContractsChangeLogService changeLog,
    IConfiguration configuration)
{
    public async Task<PurchaseContractFiscalComplementDto?> GetAsync(Guid purchaseContractKey)
    {
        var entity = await db.Context.PurchaseContractFiscalComplements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == purchaseContractKey);
        if (entity is null) return null;

        // Leitura ao vivo pelo IUsage: natureza desativada depois de escolhida continua mostrando as alíquotas.
        var usage = entity.UsageCode is { } code ? await usageService.GetByIdAsync(code) : null;

        return new PurchaseContractFiscalComplementDto
        {
            PurchaseContractKey = entity.PurchaseContractKey,
            UsageCode = entity.UsageCode,
            UsageName = usage?.Name,
            FunruralInssRate = usage?.FunruralInssRate,
            FunruralRatRate = usage?.FunruralRatRate,
            FunruralSenarRate = usage?.FunruralSenarRate,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy,
            IsComplete = entity.UsageCode.HasValue,
        };
    }

    public async Task<PurchaseContractFiscalComplementDto> SetAsync(Guid purchaseContractKey, int? usageCode, string userName)
    {
        if (!await permissions.HasAsync(userName, PermissionCodes.PurchaseContractFiscalEdit))
            throw new DefaultException("Você não tem permissão para alterar o complemento fiscal do contrato.");

        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException("Complemento fiscal do contrato de compra não se aplica ao modo SAPB1.");

        var status = await db.Context.PurchaseContracts.AsNoTracking()
            .Where(x => x.Key == purchaseContractKey)
            .Select(x => (ContractStatus?)x.Status)
            .FirstOrDefaultAsync()
            ?? throw new NotFoundException($"Purchase contract not found key {purchaseContractKey}");

        if (status is ContractStatus.Finished or ContractStatus.Canceled)
            throw new DefaultException("Contrato encerrado ou cancelado: o complemento fiscal não pode ser alterado.");

        UsageModel? newUsage = null;
        if (usageCode is { } code)
        {
            newUsage = await usageService.GetByIdAsync(code);
            if (newUsage is null || newUsage.Inactive || newUsage.Direction != UsageDirection.Incoming)
                throw new DefaultException($"A natureza de operação {code} não é uma natureza de entrada ativa.");
        }
        else if (status == ContractStatus.Approved)
        {
            // A aprovação exigiu a natureza; limpar reabriria a condição sem passar por ela de novo.
            throw new DefaultException("Contrato aprovado precisa de natureza de operação: escolha outra em vez de limpar.");
        }

        var entity = await db.Context.PurchaseContractFiscalComplements
            .FirstOrDefaultAsync(x => x.PurchaseContractKey == purchaseContractKey);
        if (entity is null)
        {
            entity = new PurchaseContractFiscalComplement { PurchaseContractKey = purchaseContractKey };
            await db.Context.PurchaseContractFiscalComplements.AddAsync(entity);
        }

        if (entity.UsageCode != usageCode)
        {
            var oldUsage = entity.UsageCode is { } oldCode ? await usageService.GetByIdAsync(oldCode) : null;
            changeLog.Register(purchaseContractKey, ContractChangeLogFields.Usage,
                Describe(entity.UsageCode, oldUsage), Describe(usageCode, newUsage), userName);
        }

        entity.UsageCode = usageCode;
        entity.UpdatedAt = DateTime.Now;
        entity.UpdatedBy = userName;

        await db.SaveChangesAsync();
        return (await GetAsync(purchaseContractKey))!;
    }

    private static string? Describe(int? code, UsageModel? usage) =>
        code is null ? null : $"{code} - {usage?.Name}";
}
