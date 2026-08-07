using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Amarração da linha do documento de entrada ao contrato de compra.
///
/// É REFERÊNCIA, não efeito: nenhuma allocation é criada e nenhum saldo de contrato muda. O saldo
/// físico continua sendo movido só pelo romaneio.
/// </summary>
public class PurchaseInvoiceContractLinkTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task<PurchaseContract> SeedContractAsync(
        string cardCode = "F0001",
        string itemCode = "SOJA",
        ContractStatus status = ContractStatus.Approved)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "CC-001",
            CardCode = cardCode,
            ItemCode = itemCode,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "A1",
            Status = status,
            TotalVolume = 1000m,
        };

        _db.Context.PurchaseContracts.Add(contract);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        return contract;
    }

    [Fact]
    public async Task Line_persists_and_reloads_the_contract_key()
    {
        var contract = await SeedContractAsync();

        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contract.Key,
        });

        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseInvoicesItems
            .AsNoTracking().FirstAsync(x => x.PurchaseInvoiceKey == invoice.Key);

        Assert.Equal(contract.Key, reloaded.PurchaseContractKey);
    }

    [Fact]
    public async Task Line_without_a_contract_is_valid()
    {
        // NF de insumo, serviço ou frete não tem contrato — e a linha importada de XML nasce sem
        // vínculo, porque o XML não o carrega.
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m,
        });

        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var reloaded = await _db.Context.PurchaseInvoicesItems
            .AsNoTracking().FirstAsync(x => x.PurchaseInvoiceKey == invoice.Key);

        Assert.Null(reloaded.PurchaseContractKey);
    }

    private static PurchaseInvoice NewInvoice(Guid? contractKey, string cardCode = "F0001")
    {
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = cardCode };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            ItemCode = "SOJA",
            ItemName = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contractKey,
        });
        return invoice;
    }

    private PurchaseInvoicesCreateService CreateService() =>
        new(_db,
            new FakeBusinessPartnerService(
                names: new Dictionary<string, string> { ["F0001"] = "PRODUTOR TESTE" }),
            new FakeItemService(
                names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }));

    [Fact]
    public async Task Contract_of_another_supplier_is_refused()
    {
        var contract = await SeedContractAsync(cardCode: "F0002");

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));

        // A mensagem precisa apontar a LINHA: numa NF de várias linhas, "contrato de outro
        // fornecedor" sozinho não diz qual delas está inconsistente.
        Assert.Contains("SOJA", ex.Message);
    }

    [Fact]
    public async Task Contract_of_another_product_is_refused()
    {
        var contract = await SeedContractAsync(itemCode: "MILHO");

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));

        Assert.Contains("SOJA", ex.Message);
    }

    [Fact]
    public async Task Contract_in_draft_is_refused()
    {
        // Só Approved e Finished podem lastrear uma NF.
        var contract = await SeedContractAsync(status: ContractStatus.Draft);

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester"));

        Assert.Contains("SOJA", ex.Message);
    }

    [Fact]
    public async Task Finished_contract_is_accepted()
    {
        // A NF chega com frequência DEPOIS do contrato encerrado — é o caso que a conciliação
        // precisa cobrir.
        var contract = await SeedContractAsync(status: ContractStatus.Finished);

        await CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester");

        var line = await _db.Context.PurchaseInvoicesItems.AsNoTracking().FirstAsync();
        Assert.Equal(contract.Key, line.PurchaseContractKey);
    }

    [Fact]
    public async Task Unknown_contract_is_refused()
    {
        await Assert.ThrowsAsync<DefaultException>(
            () => CreateService().ExecuteAsync(NewInvoice(Guid.NewGuid()), "tester"));
    }

    [Fact]
    public async Task Binding_a_contract_does_not_change_its_balance()
    {
        // O ponto da feature: é REFERÊNCIA, não efeito.
        var contract = await SeedContractAsync();
        var volumeBefore = contract.TotalVolume;

        await CreateService().ExecuteAsync(NewInvoice(contract.Key), "tester");

        var reloaded = await _db.Context.PurchaseContracts
            .AsNoTracking().FirstAsync(x => x.Key == contract.Key);

        Assert.Equal(volumeBefore, reloaded.TotalVolume);
        Assert.Empty(await _db.Context.PurchaseContractsAllocations.ToListAsync());
    }

    [Fact]
    public async Task Changing_the_issuer_with_a_bound_contract_is_refused()
    {
        var contract = await SeedContractAsync();

        var invoice = NewInvoice(contract.Key);
        await CreateService().ExecuteAsync(invoice, "tester");
        _db.Context.ChangeTracker.Clear();

        var incoming = new PurchaseInvoice { CardCode = "F0002" };
        incoming.AddItem(new PurchaseInvoiceItem
        {
            Key = invoice.Items.First().Key,
            ItemCode = "SOJA",
            Quantity = 10m,
            UnitPrice = 1m,
            PurchaseContractKey = contract.Key,
        });

        var updateService = new PurchaseInvoicesUpdateService(
            _db,
            new FakeBusinessPartnerService(names: new Dictionary<string, string>
            {
                ["F0001"] = "PRODUTOR TESTE",
                ["F0002"] = "OUTRO PARCEIRO",
            }),
            new FakeItemService(
                names: new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }));

        await Assert.ThrowsAsync<DefaultException>(
            () => updateService.ExecuteAsync(invoice.Key, incoming, "tester"));
    }
}
