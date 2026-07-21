using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PurchaseContractsCloseReopenServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

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

    private async Task<PurchaseContract> SeedAsync(ContractStatus status)
    {
        var pc = NewContract(status);
        _db.Context.PurchaseContracts.Add(pc);
        await _db.Context.SaveChangesAsync();
        return pc;
    }

    private async Task<PurchaseContract> ReloadAsync(Guid key) =>
        await _db.Context.PurchaseContracts.AsNoTracking().SingleAsync(x => x.Key == key);

    private PurchaseContractsCloseService CloseService() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context));

    private async Task<PurchaseContract> SeedPafAsync(
        decimal totalVolume,
        decimal shippedQuantity,
        params (decimal Volume, PriceFixationStatus Status)[] fixations)
        => await SeedPafAsync(totalVolume, shippedQuantity, shippedQuantity, fixations);

    private async Task<PurchaseContract> SeedPafAsync(
        decimal totalVolume,
        decimal releasedQuantity,
        decimal shippedQuantity,
        params (decimal Volume, PriceFixationStatus Status)[] fixations)
    {
        var contract = NewContract(ContractStatus.Approved);
        contract.Type = ContractType.ToBeDetermined;
        contract.TotalVolume = totalVolume;

        _db.Context.PurchaseContracts.Add(contract);

        if (releasedQuantity > 0)
        {
            _db.Context.ShipmentReleases.Add(new ShipmentRelease
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                DeliveryLocationCode = "01",
                ReleasedQuantity = releasedQuantity,
                ShippedQuantity = shippedQuantity,
                Status = ReleaseStatus.Actived,
            });
        }

        foreach (var (volume, status) in fixations)
        {
            _db.Context.PurchaseContractsPriceFixations.Add(new PurchaseContractPriceFixation
            {
                Key = Guid.NewGuid(),
                PurchaseContractKey = contract.Key,
                FixationVolume = volume,
                FixationPrice = 2m,
                Status = status,
            });
        }

        await _db.Context.SaveChangesAsync();
        return contract;
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeFullyConfirmed_Succeeds()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_Paf_UndeliveredBalance_DoesNotBlock()
    {
        // Contratou 100.000, entregou 60.000, fixou os 60.000 entregues.
        // Os 40.000 nunca entregues não impedem o fechamento.
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeNotFullyFixed_Throws()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m, (40_000m, PriceFixationStatus.Confirmed));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_DeliveredVolumeCoveredOnlyByInApproval_Throws()
    {
        // 60.000 entregues, cobertos apenas por fixação ainda não aprovada:
        // fechar aqui seria encerrar o contrato sem preço definido.
        var pc = await SeedPafAsync(100_000m, 60_000m, (60_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_WithPendingFixation_Throws()
    {
        var pc = await SeedPafAsync(100_000m, 60_000m,
            (60_000m, PriceFixationStatus.Confirmed),
            (10_000m, PriceFixationStatus.InApproval));

        await Assert.ThrowsAsync<ApplicationException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Close_Paf_ReleasedButNotShipped_DoesNotBlock()
    {
        // Liberação ativa de 60.000 kg com apenas 10.000 kg romaneados.
        // Só os 10.000 que entraram fisicamente exigem preço fixado.
        // Se a guarda usasse TotalShipmentReleases (= ReleasedQuantity), exigiria 60.000.
        var pc = await SeedPafAsync(100_000m, 60_000m, 10_000m,
            (10_000m, PriceFixationStatus.Confirmed));

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_FixedContract_IgnoresFixationGuard()
    {
        // Contrato de preço fixo não passa pela guarda nova.
        var pc = await SeedAsync(ContractStatus.Approved);

        await CloseService().ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Finished, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Close_ApprovedContract_BecomesFinished_AndRecordsUser()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await CloseService().ExecuteAsync(pc.Key, "paulo.penalva");

        var contract = await ReloadAsync(pc.Key);
        Assert.Equal(ContractStatus.Finished, contract.Status);
        Assert.Equal("paulo.penalva", contract.UpdatedBy);
    }

    [Fact]
    public async Task Close_NonApprovedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Draft);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CloseService().ExecuteAsync(pc.Key, "tester"));
    }

    [Fact]
    public async Task Reopen_FinishedContract_BecomesApproved()
    {
        var pc = await SeedAsync(ContractStatus.Finished);

        await new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester");

        Assert.Equal(ContractStatus.Approved, (await ReloadAsync(pc.Key)).Status);
    }

    [Fact]
    public async Task Reopen_NonFinishedContract_Throws()
    {
        var pc = await SeedAsync(ContractStatus.Approved);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            new PurchaseContractsReopenService(_db.Context).ExecuteAsync(pc.Key, "tester"));
    }
}
