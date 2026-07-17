using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsRecalculateBalanceService(AppDbContext context)
{
    public async Task<PurchaseContractRecalcResultDto> ExecuteAsync(Guid key)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado não participa do recálculo de saldo.");

        var result = await RecalculateAsync(contract);
        await context.SaveChangesAsync();
        return result;
    }

    public async Task<PurchaseContractRecalcAllResultDto> ExecuteAllAsync()
    {
        var contracts = await context.PurchaseContracts
            .Where(x => x.Status != ContractStatus.Finished)
            .ToListAsync();

        var changes = new List<PurchaseContractRecalcResultDto>();
        foreach (var contract in contracts)
        {
            var result = await RecalculateAsync(contract);
            if (result.Changed)
                changes.Add(result);
        }

        await context.SaveChangesAsync();

        return new PurchaseContractRecalcAllResultDto
        {
            Scanned = contracts.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<PurchaseContractRecalcResultDto> RecalculateAsync(PurchaseContract contract)
    {
        // Σ com sinal — igual ao runtime/backfill.
        var newAllocated = await context.PurchaseContractsAllocations
            .Where(a => a.PurchaseContractKey == contract.Key)
            .SumAsync(a => a.Volume);

        var previousAllocated = contract.AllocatedVolume;
        var previousAvaiable = contract.AvaiableVolume;

        contract.AllocatedVolume = newAllocated;

        return new PurchaseContractRecalcResultDto
        {
            Key = contract.Key,
            Code = contract.Code,
            PreviousAllocatedVolume = previousAllocated,
            NewAllocatedVolume = newAllocated,
            PreviousAvaiableVolume = previousAvaiable,
            NewAvaiableVolume = contract.AvaiableVolume,
            Changed = previousAllocated != newAllocated,
        };
    }
}
