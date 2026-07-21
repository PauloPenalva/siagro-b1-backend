using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A fixação automática de um contrato de preço fixo representa um preço JÁ acordado na
/// negociação — não é um pedido de fixação aguardando a diretoria. Ela precisa nascer
/// <see cref="PriceFixationStatus.Confirmed"/>, senão:
///   1. TotalPrice (que conta só Confirmed) fica zero, e com ele TotalTax; e
///   2. ela polui a caixa de entrada de aprovação com item que ninguém deve aprovar.
/// </summary>
public class FixedContractAutoFixationTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static PurchaseContract NewFixedContract() => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-FIXO",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 600_000m,
        StandardPrice = 1.033333m,
        Type = ContractType.Fixed,
        Status = ContractStatus.Draft,
    };

    [Fact]
    public async Task FixedContract_AutoFixationConfirmed_KeepsTotalPriceNonZero()
    {
        var contract = NewFixedContract();
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = contract.TotalVolume,
            FixationPrice = contract.StandardPrice,
            Status = PriceFixationStatus.Confirmed,
        });
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // 600.000 × 1,033333 = 619.999,80 — igual ao TotalStandard.
        Assert.Equal(619_999.80m, reloaded.TotalPrice);
        Assert.Equal(reloaded.TotalStandard, reloaded.TotalPrice);
    }

    [Fact]
    public async Task FixedContract_AutoFixationInApproval_WouldZeroTotalPrice()
    {
        // Documenta exatamente a regressão observada em produção-dev: 789 fixações
        // automáticas em InApproval zeravam TotalPrice e TotalTax de todo contrato fixo.
        var contract = NewFixedContract();
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = contract.TotalVolume,
            FixationPrice = contract.StandardPrice,
            Status = PriceFixationStatus.InApproval,
        });
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(0m, reloaded.TotalPrice);
        Assert.NotEqual(reloaded.TotalStandard, reloaded.TotalPrice);
    }

    [Fact]
    public async Task PendingQueue_ExcludesFixedContractFixations()
    {
        // A fila da diretoria é só para PAF: contrato fixo não tem preço a aprovar.
        var fixedContract = NewFixedContract();
        var pafContract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-PAF",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Approved,
        };

        _db.Context.PurchaseContracts.AddRange(fixedContract, pafContract);
        _db.Context.PurchaseContractsPriceFixations.AddRange(
            new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = fixedContract.Key,
                FixationVolume = 600_000m,
                FixationPrice = 1.033333m,
                Status = PriceFixationStatus.InApproval,
            },
            new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = pafContract.Key,
                FixationVolume = 30_000m,
                FixationPrice = 2.5m,
                Status = PriceFixationStatus.InApproval,
            });
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var pending = await new Services.PurchaseContracts.PurchaseContractsPriceFixationsGetService(
                _db.Context,
                Microsoft.Extensions.Logging.Abstractions
                    .NullLogger<Services.PurchaseContracts.PurchaseContractsPriceFixationsGetService>.Instance)
            .QueryPending()
            .ToListAsync();

        Assert.Single(pending);
        Assert.Equal(pafContract.Key, pending[0].PurchaseContractKey);
    }
}
