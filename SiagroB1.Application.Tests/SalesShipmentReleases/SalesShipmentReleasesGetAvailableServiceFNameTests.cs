using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesShipmentReleases;

/// <summary>
/// Cobre a coluna "Nome Fantasia" do diálogo de faturamento (<c>/shipment-billing</c>).
/// </summary>
/// <remarks>
/// O contrato guarda um SNAPSHOT de <c>CardFName</c> gravado na criação, e até 27/03/2026 a
/// entidade do SAP lia <c>OCRD.CardFName</c> (o "Nome estrangeiro", vazio na maioria dos
/// parceiros brasileiros) em vez de <c>OCRD.AliasName</c>. Contrato criado antes disso ficou
/// com o snapshot vazio para sempre — e cadastrar a fantasia no SAP depois também não corrigia
/// contrato nenhum. Por isso a coluna lê o parceiro VIVO, não o snapshot.
/// </remarks>
public class SalesShipmentReleasesGetAvailableServiceFNameTests
{
    private const string ItemCode = "MILHO";
    private const string CardCode = "C006393";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <param name="snapshotFName">O que ficou gravado no contrato — nulo nos contratos antigos.</param>
    private async Task SeedAsync(string? snapshotFName)
    {
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-1", CardCode = CardCode,
            CardName = "DOUGLAS KUMANO E OUTROS", CardFName = snapshotFName,
            ItemCode = ItemCode, ItemName = "Milho", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", TotalVolume = 10000m, Price = 50m,
            Status = ContractStatus.Approved,
        };

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesShipmentReleases.Add(new SalesShipmentRelease
        {
            Key = Guid.NewGuid(), SalesContractKey = contract.Key, DeliveryLocationCode = "01",
            ReleasedQuantity = 1000m, ShippedQuantity = 0m, Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();
    }

    private SalesShipmentReleasesGetAvailableService Service(params SupplierInfo[] partners) =>
        Service(new FakeBusinessPartnerService(suppliers: partners.ToDictionary(p => p.CardCode)));

    private SalesShipmentReleasesGetAvailableService Service(FakeBusinessPartnerService partners) =>
        new(_db, partners, NullLogger<SalesShipmentReleasesGetAvailableService>.Instance);

    [Fact]
    public async Task CardFName_ComesFromPartner_WhenContractSnapshotIsEmpty()
    {
        await SeedAsync(snapshotFName: null);

        var partner = new SupplierInfo
        {
            CardCode = CardCode, CardName = "DOUGLAS KUMANO E OUTROS",
            CardFName = "DOUGLAS KUMANO E OUTROS",
        };

        var row = Assert.Single(await Service(partner).ExecuteAsync(ItemCode));

        Assert.Equal("DOUGLAS KUMANO E OUTROS", row.CardFName);
    }

    [Fact]
    public async Task CardFName_PrefersPartnerOverStaleContractSnapshot()
    {
        await SeedAsync(snapshotFName: "FANTASIA ANTIGA");

        var partner = new SupplierInfo
        {
            CardCode = CardCode, CardName = "DOUGLAS KUMANO E OUTROS",
            CardFName = "FANTASIA ATUAL",
        };

        var row = Assert.Single(await Service(partner).ExecuteAsync(ItemCode));

        Assert.Equal("FANTASIA ATUAL", row.CardFName);
    }

    [Fact]
    public async Task CardFName_FallsBackToCardName_WhenPartnerHasNoAliasName()
    {
        await SeedAsync(snapshotFName: null);

        var partner = new SupplierInfo
        {
            CardCode = CardCode, CardName = "DOUGLAS KUMANO E OUTROS", CardFName = null,
        };

        var row = Assert.Single(await Service(partner).ExecuteAsync(ItemCode));

        Assert.Equal("DOUGLAS KUMANO E OUTROS", row.CardFName);
    }

    /// <summary>
    /// Parceiro fora do alcance (removido do SAP, ou modo STANDALONE sem cadastro): a coluna
    /// cai para o que o contrato guardou, e nunca fica vazia.
    /// </summary>
    [Fact]
    public async Task CardFName_KeepsContractSnapshot_WhenPartnerIsMissing()
    {
        await SeedAsync(snapshotFName: "FANTASIA DO CONTRATO");

        var row = Assert.Single(await Service().ExecuteAsync(ItemCode));

        Assert.Equal("FANTASIA DO CONTRATO", row.CardFName);
    }

    [Fact]
    public async Task CardFName_FallsBackToContractCardName_WhenNothingElseIsAvailable()
    {
        await SeedAsync(snapshotFName: null);

        var row = Assert.Single(await Service().ExecuteAsync(ItemCode));

        Assert.Equal("DOUGLAS KUMANO E OUTROS", row.CardFName);
    }

    /// <summary>
    /// SAP fora do ar não pode derrubar o faturamento: a coluna é informativa, então a lista
    /// sai com o que o contrato guardou em vez de a chamada inteira falhar.
    /// </summary>
    [Fact]
    public async Task ListStillLoads_WhenPartnerLookupFails()
    {
        await SeedAsync(snapshotFName: "FANTASIA DO CONTRATO");

        var row = Assert.Single(
            await Service(new FakeBusinessPartnerService(failOnLoadSuppliers: true))
                .ExecuteAsync(ItemCode));

        Assert.Equal("FANTASIA DO CONTRATO", row.CardFName);
    }
}
