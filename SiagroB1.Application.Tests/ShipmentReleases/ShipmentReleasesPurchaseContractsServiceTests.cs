using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Cobre a segunda tela da Expedição de Grãos (seleção do contrato de compra): a linha
/// precisa exibir o fornecedor mesmo quando o cadastro de parceiros não devolve o CardCode,
/// caso da maioria dos fornecedores do Yokotobi.
/// </summary>
public class ShipmentReleasesPurchaseContractsServiceTests
{
    private const string ItemCode = "SOJA";
    private const string WarehouseCode = "ARM01";
    private const string CardCode = "F003595";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedAsync(string? contractCardName)
    {
        var branch = new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ" };
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = CardCode, CardName = contractCardName,
            ItemCode = ItemCode, ItemName = "Soja", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", DeliveryLocationCode = WarehouseCode, TotalVolume = 1000m,
        };

        _db.Context.Branchs.Add(branch);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, BranchCode = branch.Code,
            DeliveryLocationCode = WarehouseCode, DeliveryLocationName = "Armazem 01",
            ReleasedQuantity = 100m, ShippedQuantity = 30m, Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        return contract;
    }

    private ShipmentReleasesPurchaseContractsService Service(FakeBusinessPartnerService partners) =>
        new(_db, partners, NullLogger<ShipmentReleasesPurchaseContractsService>.Instance);

    [Fact]
    public async Task FName_UsesContractCardName_WhenPartnerIsNotResolved()
    {
        await SeedAsync("SERGIO YUKIO SUKESSADA");

        var result = await Service(new FakeBusinessPartnerService()).ExecuteAsync(ItemCode, WarehouseCode);

        var row = Assert.Single(result);
        Assert.Equal("SERGIO YUKIO SUKESSADA", row.FName);
        Assert.Equal(70m, row.AvailableQuantity); // 100 − 30
    }

    [Fact]
    public async Task PartnerData_EnrichesRow_WhenPartnerIsResolved()
    {
        await SeedAsync("NOME NO CONTRATO");

        var partners = new FakeBusinessPartnerService(suppliers: new Dictionary<string, SupplierInfo>
        {
            [CardCode] = new()
            {
                CardCode = CardCode, CardName = "NOME NO CADASTRO", TaxId = "12345678000199",
                Notes = "observacao", Address = new SupplierAddress { City = "Itarare", State = "SP" },
            },
        });

        var row = Assert.Single(await Service(partners).ExecuteAsync(ItemCode, WarehouseCode));

        Assert.Equal("NOME NO CONTRATO", row.FName);
        Assert.Equal("12345678000199", row.TaxId);
        Assert.Equal("Itarare", row.City);
        Assert.Equal("SP", row.State);
        Assert.Equal("observacao", row.Notes);
        Assert.Equal(CardCode, row.FCode);
    }

    [Fact]
    public async Task FName_FallsBackToPartner_WhenContractHasNoCardName()
    {
        await SeedAsync(null);

        var partners = new FakeBusinessPartnerService(suppliers: new Dictionary<string, SupplierInfo>
        {
            [CardCode] = new() { CardCode = CardCode, CardName = "NOME NO CADASTRO" },
        });

        var row = Assert.Single(await Service(partners).ExecuteAsync(ItemCode, WarehouseCode));

        Assert.Equal("NOME NO CADASTRO", row.FName);
    }
}
