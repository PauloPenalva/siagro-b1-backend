using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// A Alocação de Romaneios precisa aceitar contrato de fornecedor (e armazém) diferente
/// do romaneio, para corrigir erro de lançamento. A lista traz todos os contratos do
/// produto, com os do fornecedor do romaneio primeiro.
/// </summary>
public class PurchaseContractsGetAvaiablesListSupplierTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsGetService Service() =>
        new(_db, NullLogger<PurchaseContractsGetService>.Instance);

    private void Add(string code, string cardCode, string itemCode, string location = "01")
    {
        _db.Context.PurchaseContracts.Add(new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = $"NOME {cardCode}",
            ItemCode = itemCode, UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            DeliveryLocationCode = location, DeliveryLocationName = $"ARMAZEM {location}",
            TotalVolume = 1000m, AllocatedVolume = 0m, Status = ContractStatus.Approved,
        });
    }

    [Fact]
    public async Task ContractOfOtherSupplier_SameItem_IsListed()
    {
        Add("PC-001", "F0002", "SOJA");
        await _db.Context.SaveChangesAsync();

        var dto = Assert.Single(await Service().GetAvaiablesPurchaseContractsAsync("F0001", "SOJA"));

        Assert.Equal("PC-001", dto.Code);
    }

    [Fact]
    public async Task ContractOfOtherItem_IsNotListed()
    {
        Add("PC-001", "F0001", "MILHO");
        await _db.Context.SaveChangesAsync();

        Assert.Empty(await Service().GetAvaiablesPurchaseContractsAsync("F0001", "SOJA"));
    }

    [Fact]
    public async Task SameSupplierContracts_ComeFirst()
    {
        Add("PC-001", "F0002", "SOJA");
        Add("PC-002", "F0001", "SOJA");
        Add("PC-003", "F0003", "SOJA");
        Add("PC-004", "F0001", "SOJA");
        await _db.Context.SaveChangesAsync();

        var codes = (await Service().GetAvaiablesPurchaseContractsAsync("F0001", "SOJA")).Select(x => x.Code);

        Assert.Equal(new[] { "PC-002", "PC-004", "PC-001", "PC-003" }, codes);
    }

    [Fact]
    public async Task Dto_CarriesSupplierAndDeliveryLocation()
    {
        Add("PC-001", "F0002", "SOJA", "07");
        await _db.Context.SaveChangesAsync();

        var dto = Assert.Single(await Service().GetAvaiablesPurchaseContractsAsync("F0001", "SOJA"));

        Assert.Equal("F0002", dto.CardCode);
        Assert.Equal("NOME F0002", dto.CardName);
        Assert.Equal("07", dto.DeliveryLocationCode);
        Assert.Equal("ARMAZEM 07", dto.DeliveryLocationName);
    }

    [Theory]
    [InlineData(1000, 0, false)]          // sem saldo
    [InlineData(999.995, 0, false)]       // 0,005 arredonda para 0,00 (ToEven): fora
    [InlineData(999.994, 0, true)]        // 0,006 arredonda para 0,01: dentro
    [InlineData(600, 400, false)]         // o volume lavado também sai do saldo
    public async Task OnlyContractsWithRoundedAvaiableVolumeAboveZero_AreListed(
        double allocated, double washedOut, bool listed)
    {
        Add("PC-001", "F0001", "SOJA");
        var contract = _db.Context.PurchaseContracts.Local.Single();
        contract.AllocatedVolume = (decimal)allocated;
        contract.WashedOutVolume = (decimal)washedOut;
        await _db.Context.SaveChangesAsync();

        var list = await Service().GetAvaiablesPurchaseContractsAsync("F0001", "SOJA");

        Assert.Equal(listed, list.Count == 1);
        if (listed)
            Assert.Equal(contract.AvaiableVolume, list[0].AvaiableVolume);
    }
}
