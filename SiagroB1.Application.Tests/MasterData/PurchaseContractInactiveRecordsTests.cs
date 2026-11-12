using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.MasterData;

/// <summary>
/// GAC-1196 no contrato de compra: cadastro inativo no SAP não entra em edição que o ESCOLHE,
/// mas o contrato em andamento que já apontava para ele (inativado depois) continua editável.
/// </summary>
public class PurchaseContractInactiveRecordsTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly FakeBusinessPartnerService _partners = new(new Dictionary<string, string>
    {
        ["F0001"] = "FORNECEDOR", ["F0002"] = "OUTRO FORNECEDOR",
    });

    private readonly FakeWarehouseService _warehouses = new(new Dictionary<string, string>
    {
        ["01"] = "ARMAZEM 01", ["02"] = "ARMAZEM 02",
    });

    private PurchaseContractsUpdateService Service() => new(
        _db.Context,
        _partners,
        new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
        _warehouses,
        new FakeAgentService(new Dictionary<int, string> { [1] = "AGENTE" }),
        TestNotificationOutbox.For(_db.Context),
        NullLogger<PurchaseContractsUpdateService>.Instance);

    private async Task<PurchaseContract> SeedAsync()
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
            AgentCode = 1,
            TotalVolume = 100_000m,
            StandardPrice = 2m,
            Type = ContractType.ToBeDetermined,
            Status = ContractStatus.Draft,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    [Fact]
    public async Task Contract_in_progress_keeps_saving_after_its_supplier_was_inactivated()
    {
        var contract = await SeedAsync();
        _partners.Inactive.Add("F0001");
        _warehouses.Inactive.Add("01");

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.TotalVolume = 250_000m;

        await Service().ExecuteAsync(contract.Key, tracked, "joao");

        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().FirstAsync(c => c.Key == contract.Key);
        Assert.Equal(250_000m, reloaded.TotalVolume);
        Assert.Equal("F0001", reloaded.CardCode);
    }

    /// <summary>Forma do PATCH: a instância rastreada já chega com o código novo.</summary>
    [Fact]
    public async Task Switching_the_supplier_to_an_inactive_one_is_blocked()
    {
        var contract = await SeedAsync();
        _partners.Inactive.Add("F0002");

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.CardCode = "F0002";

        var error = await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(contract.Key, tracked, "joao"));

        Assert.Contains("O fornecedor F0002 - OUTRO FORNECEDOR está inativo no SAP", error.Message);
    }

    [Fact]
    public async Task Switching_the_delivery_location_to_an_inactive_warehouse_is_blocked()
    {
        var contract = await SeedAsync();
        _warehouses.Inactive.Add("02");

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.DeliveryLocationCode = "02";

        var error = await Assert.ThrowsAnyAsync<Exception>(() => Service().ExecuteAsync(contract.Key, tracked, "joao"));

        Assert.Contains("O local de entrega 02 - ARMAZEM 02 está inativo no SAP", error.Message);
    }

    [Fact]
    public async Task Switching_to_an_active_supplier_still_works()
    {
        var contract = await SeedAsync();
        _partners.Inactive.Add("F0001");

        var tracked = await _db.Context.PurchaseContracts.FirstAsync(c => c.Key == contract.Key);
        tracked.CardCode = "F0002";

        await Service().ExecuteAsync(contract.Key, tracked, "joao");

        var reloaded = await _db.Context.PurchaseContracts.AsNoTracking().FirstAsync(c => c.Key == contract.Key);
        Assert.Equal("OUTRO FORNECEDOR", reloaded.CardName);
    }
}
