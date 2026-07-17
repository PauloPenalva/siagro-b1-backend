using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.Infra;

public class FinishedContractMutationGuardInterceptorTests
{
    private static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(new FinishedContractMutationGuardInterceptor())
            .Options;

        return new AppDbContext(options);
    }

    private static PurchaseContract NewContract(ContractStatus status) => new()
    {
        Key = Guid.NewGuid(),
        Code = "PC-001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1000m,
        Status = status,
    };

    [Fact]
    public async Task AddingBrokerToFinishedContract_Throws()
    {
        await using var ctx = NewContext();
        var pc = NewContract(ContractStatus.Finished);
        ctx.PurchaseContracts.Add(pc);
        await ctx.SaveChangesAsync();

        ctx.PurchaseContractsBrokers.Add(new PurchaseContractBroker
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = pc.Key,
            CardCode = "B0001",
        });

        await Assert.ThrowsAsync<DefaultException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task AddingBrokerToApprovedContract_Succeeds()
    {
        await using var ctx = NewContext();
        var pc = NewContract(ContractStatus.Approved);
        ctx.PurchaseContracts.Add(pc);
        await ctx.SaveChangesAsync();

        ctx.PurchaseContractsBrokers.Add(new PurchaseContractBroker
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = pc.Key,
            CardCode = "B0001",
        });

        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.PurchaseContractsBrokers.CountAsync());
    }

    [Fact]
    public async Task DeletingPriceFixationOfFinishedContract_Throws()
    {
        await using var ctx = NewContext();
        var pc = NewContract(ContractStatus.Approved);
        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = pc.Key,
        };
        ctx.PurchaseContracts.Add(pc);
        ctx.PurchaseContractsPriceFixations.Add(fixation);
        await ctx.SaveChangesAsync();

        // encerra o contrato
        pc.Status = ContractStatus.Finished;
        await ctx.SaveChangesAsync();

        ctx.PurchaseContractsPriceFixations.Remove(fixation);

        await Assert.ThrowsAsync<DefaultException>(() => ctx.SaveChangesAsync());
    }
}
