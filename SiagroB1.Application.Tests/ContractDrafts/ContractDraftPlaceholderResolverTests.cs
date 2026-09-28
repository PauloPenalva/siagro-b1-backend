using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ContractDrafts;

/// <summary>
/// O resolver é a única ponte entre o contrato e o texto. Parceiro vem SEMPRE por
/// IBusinessPartnerService (em SAPB1 a tabela local está vazia); empresa vem de BRANCHS.
/// </summary>
public class ContractDraftPlaceholderResolverTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private ContractDraftPlaceholderResolver Resolver() => new(
        _db.Context,
        new FakeBusinessPartnerService(
            names: new() { ["F0001"] = "FAZENDA BOA VISTA LTDA", ["C0001"] = "TRADING SUL SA" },
            taxIds: new() { ["F0001"] = "12345678000190" },
            addresses: new()
            {
                ["F0001"] =
                [
                    new AddressModel { CardCode = "F0001", AddressName = "FATURAMENTO", AdresType = "B",
                        Street = "Rod. BR-163, km 12", Block = "Zona Rural", ZipCode = "78700000",
                        City = "Rondonópolis", State = "MT" },
                ],
            }));

    private async Task SeedMasterDataAsync()
    {
        _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ", TaxId = "98765432000110" });
        _db.Context.HarvestSeasons.Add(new HarvestSeason { Code = "24/25", Name = "Safra 2024/2025" });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { Name = "Ana Diretora", TaxId = "11111111111", Email = "ana@empresa.com", Role = SignatoryRole.SignAsParty, Order = 1 });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { Name = "Inativo", TaxId = "22222222222", Email = "x@empresa.com", Active = false });
        _db.Context.CompanySignatories.Add(new CompanySignatory
            { BranchCode = "02", Name = "Outra filial", TaxId = "33333333333", Email = "f2@empresa.com" });
        _db.Context.BusinessPartnerSignatories.Add(new BusinessPartnerSignatory
            { CardCode = "F0001", Name = "João Produtor", TaxId = "44444444444", Email = "joao@fazenda.com", Role = SignatoryRole.SignAsParty });
        await _db.Context.SaveChangesAsync();
    }

    private static PurchaseContract Purchase() => new()
    {
        Key = Guid.NewGuid(), Code = "PC-000123", Complement = "Lote A", BranchCode = "01",
        CreationDate = new DateTime(2026, 9, 21), CardCode = "F0001", ItemCode = "SOJA", ItemName = "SOJA EM GRÃOS",
        UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", TotalVolume = 1_500_000m, StandardPrice = 2.35m,
        StandardCurrency = CurrencyType.Brl, DeliveryStartDate = new DateTime(2026, 10, 1),
        DeliveryEndDate = new DateTime(2026, 11, 30), FreightTerms = FreightTerms.Cif,
        DeliveryLocationCode = "01", DeliveryLocationName = "ARMAZÉM MATRIZ", AgentName = "Carlos Rep.",
        PaymentTerms = "30 dias após entrega", StandardCashFlowDate = new DateTime(2026, 12, 30),
        Type = ContractType.Fixed, Status = ContractStatus.Approved,
        Brokers = [new PurchaseContractBroker { CardCode = "B01", CardName = "Corretora XY", Commission = 0.5m, ComissionUmCode = "%" }],
    };

    [Fact]
    public async Task Resolves_every_purchase_placeholder_with_formatted_values()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        Assert.Equal("PC-000123", values["numero"]);
        Assert.Equal("21/09/2026", values["emissao"]);
        Assert.Equal("21 de setembro de 2026", values["emissao_extenso"]);
        Assert.Equal("FAZENDA BOA VISTA LTDA", values["fornecedor_razao_social"]);
        Assert.Equal("12.345.678/0001-90", values["fornecedor_cnpj"]);
        Assert.Equal("Rod. BR-163, km 12", values["fornecedor_endereco"]);
        Assert.Equal("Rondonópolis", values["fornecedor_cidade"]);
        Assert.Equal("MT", values["fornecedor_uf"]);
        Assert.Equal("1.500.000,000", values["quantidade"]);
        Assert.Equal("2,35", values["preco"]);
        Assert.Equal("R$", values["moeda"]);
        Assert.Equal("3.525.000,00", values["valor_total"]);
        Assert.Equal("três milhões, quinhentos e vinte e cinco mil reais", values["valor_total_extenso"]);
        Assert.Equal("CIF", values["tipo_frete"]);
        Assert.Equal("Corretora XY", values["corretor_nome"]);
        Assert.Equal("Safra 2024/2025", values["safra_descricao"]);
        Assert.Equal("98.765.432/0001-10", values["empresa_cnpj"]);
        Assert.Equal("Matriz", values["filial_nome"]);
    }

    [Fact]
    public async Task Resolver_covers_every_catalog_name()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        foreach (var name in ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Purchase))
            Assert.True(values.ContainsKey(name), $"placeholder sem valor: {name}");
    }

    [Fact]
    public async Task Signature_blocks_use_only_active_signatories_of_the_branch_or_global()
    {
        await SeedMasterDataAsync();

        var values = await Resolver().ResolveAsync(Purchase(), CancellationToken.None);

        Assert.Contains("Ana Diretora", values["assinaturas_empresa"]);
        Assert.DoesNotContain("Inativo", values["assinaturas_empresa"]);
        Assert.DoesNotContain("Outra filial", values["assinaturas_empresa"]);
        Assert.Contains("João Produtor", values["assinaturas_parceiro"]);
        Assert.Contains("444.444.444-44", values["assinaturas_parceiro"]);
    }

    [Fact]
    public async Task Unknown_partner_yields_empty_strings_not_exceptions()
    {
        await SeedMasterDataAsync();
        var contract = Purchase();
        contract.CardCode = "NAO-EXISTE";

        var values = await Resolver().ResolveAsync(contract, CancellationToken.None);

        Assert.Equal("", values["fornecedor_razao_social"]);
        Assert.Equal("", values["fornecedor_cnpj"]);
    }

    [Fact]
    public async Task Sales_uses_client_names_and_joins_delivery_locations()
    {
        await SeedMasterDataAsync();
        var contract = new SalesContract
        {
            Key = Guid.NewGuid(), Code = "SC-000045", BranchCode = "01", CreationDate = new DateTime(2026, 9, 21),
            CardCode = "C0001", ItemCode = "SOJA", ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", Volume = 500_000m, Price = 2.50m, StandardCurrency = CurrencyType.Usd,
            DeliveryStartDate = new DateTime(2026, 10, 1), DeliveryEndDate = new DateTime(2026, 10, 31),
            FreightTerms = FreightTerms.Fob, Type = ContractType.Fixed, Status = ContractStatus.Approved,
            DeliveryLocations =
            [
                new SalesContractDeliveryLocation { CardCode = "T1", CardName = "TERMINAL SANTOS" },
                new SalesContractDeliveryLocation { CardCode = "T2", CardName = "TERMINAL PARANAGUÁ" },
            ],
        };

        var values = await Resolver().ResolveAsync(contract, CancellationToken.None);

        Assert.Equal("TRADING SUL SA", values["cliente_razao_social"]);
        Assert.Equal("TERMINAL SANTOS; TERMINAL PARANAGUÁ", values["local_entrega"]);
        Assert.Equal("US$", values["moeda"]);
        Assert.Equal("1.250.000,00", values["valor_total"]);
        Assert.False(values.ContainsKey("fornecedor_cnpj"));

        foreach (var name in ContractDraftPlaceholderCatalog.NamesFor(ContractTemplateScope.Sales))
            Assert.True(values.ContainsKey(name), $"placeholder sem valor: {name}");
    }
}
