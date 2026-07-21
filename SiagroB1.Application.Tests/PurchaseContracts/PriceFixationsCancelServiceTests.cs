using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

public class PriceFixationsCancelServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsPriceFixationsCancelService Service() =>
        new(_db.Context, new PurchaseContractsFixedVolumeService(_db.Context));

    private async Task<(PurchaseContract Contract, PurchaseContractPriceFixation Fixation)> SeedAsync(
        PriceFixationStatus status = PriceFixationStatus.Confirmed,
        ContractStatus contractStatus = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25",
            DeliveryLocationCode = "01",
            TotalVolume = 100_000m,
            FixedVolume = 40_000m,
            Type = ContractType.ToBeDetermined,
            Status = contractStatus,
        };

        var fixation = new PurchaseContractPriceFixation
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            FixationVolume = 40_000m,
            FixationPrice = 2.5m,
            Status = status,
        };

        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.PurchaseContractsPriceFixations.Add(fixation);
        await _db.Context.SaveChangesAsync();

        return (contract, fixation);
    }

    [Fact]
    public async Task Cancel_ConfirmedFixation_ReturnsToInApproval_AndClearsApproval()
    {
        var (_, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.PurchaseContractsPriceFixations
            .AsNoTracking().SingleAsync(x => x.Key == fixation.Key);

        // Estorno desfaz a APROVAÇÃO: a fixação volta para a fila da diretoria,
        // não vira um registro morto. Assim dá para reaprovar, editar ou excluir.
        Assert.Equal(PriceFixationStatus.InApproval, reloaded.Status);

        // A aprovação anterior deixa de valer e não pode ficar pendurada.
        Assert.True(string.IsNullOrEmpty(reloaded.ApprovedBy));
        Assert.Null(reloaded.ApprovedAt);
        Assert.Null(reloaded.ApprovalComments);

        // Quem estornou fica registrado.
        Assert.Equal("operador", reloaded.CanceledBy);
        Assert.NotNull(reloaded.CanceledAt);
    }

    [Fact]
    public async Task Cancel_KeepsVolumeReserved()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // InApproval reserva volume igual a Confirmed: estornar NÃO devolve saldo.
        // Para devolver o volume, exclui-se a fixação (ou a diretoria a rejeita).
        Assert.Equal(40_000m, reloaded.FixedVolume);
        Assert.Equal(60_000m, reloaded.AvailableVolumeToPricing);
    }

    [Fact]
    public async Task Cancel_RemovesFixationFromTotalPrice()
    {
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var reloaded = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        Assert.Equal(0m, reloaded.TotalPrice);
    }

    [Fact]
    public async Task Cancel_InApprovalFixation_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.InApproval);

        // Fixação em aprovação se resolve por rejeição, não por estorno.
        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_AlreadyCanceled_Throws()
    {
        var (_, fixation) = await SeedAsync(status: PriceFixationStatus.Canceled);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_OnFinishedContract_Throws()
    {
        var (_, fixation) = await SeedAsync(contractStatus: ContractStatus.Finished);

        await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(fixation.Key, "operador"));
    }

    [Fact]
    public async Task Cancel_ThenReapprove_RestoresTotalPrice()
    {
        // Ciclo completo do estorno: desfaz a aprovação, o preço sai do total,
        // e a diretoria pode aprovar de novo devolvendo o valor.
        var (contract, fixation) = await SeedAsync();

        await Service().ExecuteAsync(fixation.Key, "operador");

        var afterCancel = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);
        Assert.Equal(0m, afterCancel.TotalPrice);

        await new PurchaseContractsPriceFixationsApprovalService(
                _db.Context, new PurchaseContractsFixedVolumeService(_db.Context))
            .ExecuteAsync(fixation.Key, "reaprovado", "diretoria");

        var afterReapproval = await _db.Context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .AsNoTracking().SingleAsync(x => x.Key == contract.Key);

        // 40.000 × 2,50 = 100.000
        Assert.Equal(100_000m, afterReapproval.TotalPrice);
    }
}
