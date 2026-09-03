using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesInvoices.Factories;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Numa DEVOLUÇÃO o peso do cabeçalho é derivado da soma das quantidades dos itens.
/// </summary>
/// <remarks>
/// O caso que originou esta regra (homologação Yokotobi, 28/08/2026): o operador abriu a
/// devolução pendente 000006517, digitou <b>20</b> no Peso Líquido do cabeçalho e confirmou. A
/// linha do item continuou com os <b>30</b> da origem — e é ela que alimenta o ledger, então o
/// contrato recebeu de volta os 30 cheios. Dois campos diziam "quanto voltou" e só um decidia,
/// sem nada avisando que discordavam.
/// </remarks>
public class SalesInvoicesReturnWeightTests
{
    private const string CardCode = "C0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FakeItemService Items() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private SalesInvoicesConfirmService ConfirmService() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesInvoicesUsageGuardService(
                new UsageService(_db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBalanceHookService(
                _db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private SalesInvoicesCreateService CreateService()
    {
        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });

        return new SalesInvoicesCreateService(
            _db,
            partners,
            Items(),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(_db, usages, partners),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private SalesInvoicesItemsUpdateService ItemsUpdateService() =>
        new(_db, Items(), NullLogger<SalesInvoicesUpdateService>.Instance);

    private SalesInvoicesItemsCreateService ItemsCreateService() =>
        new(_db, Items(), NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private SalesInvoicesItemsDeleteService ItemsDeleteService() =>
        new(_db, NullLogger<SalesInvoicesItemsDeleteService>.Instance);

    // ─── Seed ───

    private static SalesInvoice NewOrigin(decimal quantity, Guid contractKey)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            CardName = "CLIENTE TESTE",
            BranchCode = "01",
            InvoiceNumber = "000006506",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            GrossWeight = quantity,
            NetWeight = quantity,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            UnitPrice = 90m,
            SalesContractKey = contractKey,
        });

        return invoice;
    }

    /// <summary>
    /// O estado da homologação: origem de 30 consumindo o contrato, e a devolução PENDENTE que
    /// o botão Retornar antigo deixava editável.
    /// </summary>
    private async Task<(SalesContract Contract, SalesInvoice Origin, SalesInvoice Return)> SeedAsync(
        decimal quantity = 30m)
    {
        var contract = NewContract(totalVolume: 900_000m);
        contract.AllocatedVolume = quantity;

        var origin = NewOrigin(quantity, contract.Key);

        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesInvoices.Add(origin);
        _db.Context.Branchs.Add(
            new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });
        await _db.SaveChangesAsync();

        var billing = NewAllocation(
            contract.Key, origin.Items.Single().Key!.Value, quantity);
        billing.OwnsDeliveryDifference = true;
        _db.Context.SalesContractsAllocations.Add(billing);

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, "tester");
        returnInvoice.InvoiceNumber = "000006517";
        _db.Context.SalesInvoices.Add(returnInvoice);
        await _db.SaveChangesAsync();

        return (contract, origin, returnInvoice);
    }

    private Task<SalesInvoice> InvoiceAsync(Guid key) =>
        _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == key);

    // ─── A devolução nasce com o peso derivado ───

    /// <summary>
    /// Devolução TOTAL nasce com os pesos iguais à soma das quantidades — que no documento
    /// coerente é o mesmo número que a origem já tinha.
    /// </summary>
    [Fact]
    public void A_total_return_is_born_with_the_weights_of_its_items()
    {
        var origin = NewOrigin(30m, Guid.NewGuid());

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, "tester");

        Assert.Equal(30m, returnInvoice.NetWeight);
        Assert.Equal(30m, returnInvoice.GrossWeight);
    }

    /// <summary>
    /// Devolução PARCIAL nasce com o peso do que voltou, e não com o da carreta inteira: o peso
    /// do cabeçalho deixa de ser um número independente da linha.
    /// </summary>
    [Fact]
    public void A_partial_return_is_born_with_the_weight_of_what_came_back()
    {
        var origin = NewOrigin(30m, Guid.NewGuid());
        var originItemKey = origin.Items.Single().Key!.Value;

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(
            origin, "tester", new Dictionary<Guid, decimal> { [originItemKey] = 20m });

        Assert.Equal(20m, Assert.Single(returnInvoice.Items).Quantity);
        Assert.Equal(20m, returnInvoice.NetWeight);
        Assert.Equal(20m, returnInvoice.GrossWeight);
    }

    /// <summary>
    /// Origem cujo peso não bate com a quantidade (documento legado) não contamina a devolução:
    /// o peso dela sai da QUANTIDADE devolvida, não de um rateio do peso antigo.
    /// </summary>
    [Fact]
    public void An_incoherent_origin_does_not_carry_its_divergence_into_the_return()
    {
        var origin = NewOrigin(30m, Guid.NewGuid());
        origin.NetWeight = 27m;
        origin.GrossWeight = 27m;

        var returnInvoice = SalesInvoiceReturnFactory.CreateFrom(origin, "tester");

        Assert.Equal(30m, returnInvoice.NetWeight);
        Assert.Equal(30m, returnInvoice.GrossWeight);
    }

    /// <summary>
    /// Devolução criada à mão pela tela (Documento de Saída com tipo Devolução) também nasce
    /// com o peso derivado: ela não passa pelo <c>SalesInvoiceReturnFactory</c>, e sem isso
    /// nasceria com o peso que a tela mandou — travado, e recusado na confirmação.
    /// </summary>
    [Fact]
    public async Task A_hand_made_return_is_created_with_the_weight_of_its_items()
    {
        var (_, origin, _) = await SeedAsync();
        var originItem = origin.Items.Single();

        var handMade = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            BranchCode = "01",
            InvoiceType = SalesInvoiceType.Return,
            SalesInvoiceOriginKey = origin.Key,
            // O que a tela mandaria antes de o campo ficar travado.
            GrossWeight = 999m,
            NetWeight = 999m,
        };

        handMade.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 20m,
            UnitPrice = 90m,
            SalesInvoiceItemOriginKey = originItem.Key,
        });

        await CreateService().ExecuteAsync(handMade, "tester");

        var saved = await InvoiceAsync(handMade.Key);

        Assert.Equal(20m, saved.NetWeight);
        Assert.Equal(20m, saved.GrossWeight);
    }

    // ─── Editar a linha reescreve o peso do cabeçalho ───

    /// <summary>
    /// O caminho certo do parcial: baixar a QUANTIDADE do item leva o peso do cabeçalho junto.
    /// </summary>
    [Fact]
    public async Task Lowering_the_item_quantity_rewrites_the_return_header_weight()
    {
        var (_, _, returnInvoice) = await SeedAsync();

        var item = await _db.Context.SalesInvoicesItems
            .SingleAsync(x => x.SalesInvoiceKey == returnInvoice.Key);

        item.Quantity = 20m;

        await ItemsUpdateService().ExecuteAsync(item.Key!.Value, item, "tester");

        var saved = await InvoiceAsync(returnInvoice.Key);

        Assert.Equal(20m, saved.NetWeight);
        Assert.Equal(20m, saved.GrossWeight);
    }

    /// <summary>
    /// A contraprova: documento NORMAL não tem peso derivado. Ali o peso é o da balança e a
    /// quantidade é a faturada — podem divergir legitimamente (quebra, tara).
    /// </summary>
    [Fact]
    public async Task Lowering_an_item_of_a_normal_document_leaves_its_weight_alone()
    {
        var (_, origin, _) = await SeedAsync();

        var item = await _db.Context.SalesInvoicesItems
            .SingleAsync(x => x.SalesInvoiceKey == origin.Key);

        item.Quantity = 20m;

        await ItemsUpdateService().ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(30m, (await InvoiceAsync(origin.Key)).NetWeight);
    }

    /// <summary>Acrescentar uma linha à devolução soma no peso do cabeçalho.</summary>
    [Fact]
    public async Task Adding_an_item_to_a_return_adds_to_its_header_weight()
    {
        var (_, _, returnInvoice) = await SeedAsync();

        await ItemsCreateService().ExecuteAsync(
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(),
                SalesInvoiceKey = returnInvoice.Key,
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                Quantity = 5m,
                UnitPrice = 90m,
            },
            "tester");

        Assert.Equal(35m, (await InvoiceAsync(returnInvoice.Key)).NetWeight);
    }

    /// <summary>Remover a única linha zera o peso — o cabeçalho nunca fica com um número órfão.</summary>
    [Fact]
    public async Task Deleting_the_last_item_of_a_return_zeroes_its_header_weight()
    {
        var (_, _, returnInvoice) = await SeedAsync();

        var item = await _db.Context.SalesInvoicesItems
            .AsNoTracking()
            .SingleAsync(x => x.SalesInvoiceKey == returnInvoice.Key);

        await ItemsDeleteService().ExecuteAsync(item.Key!.Value);

        Assert.Equal(decimal.Zero, (await InvoiceAsync(returnInvoice.Key)).NetWeight);
    }

    // ─── O guard da confirmação ───

    /// <summary>
    /// O caso do Yuji, reproduzido: peso do cabeçalho em 20 e linha em 30. A confirmação recusa
    /// nomeando os DOIS números — sem isso o contrato recebe 30 de volta em silêncio.
    /// </summary>
    [Fact]
    public async Task A_return_whose_header_weight_disagrees_with_its_items_is_refused()
    {
        var (contract, _, returnInvoice) = await SeedAsync();

        // Só o caminho de fora do sistema (API crua, dado legado) consegue divergir depois da
        // derivação — aqui a divergência é gravada à mão para exercitar o guard.
        var stored = await _db.Context.SalesInvoices.SingleAsync(x => x.Key == returnInvoice.Key);
        stored.NetWeight = 20m;
        stored.GrossWeight = 20m;
        await _db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => ConfirmService().ExecuteAsync(returnInvoice.Key, "tester"));

        Assert.Contains("20,000", error.Message);
        Assert.Contains("30,000", error.Message);

        // E nada foi lançado no contrato.
        Assert.Equal(30m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }

    /// <summary>
    /// A contraprova: com os dois números de acordo a devolução confirma e devolve ao contrato
    /// exatamente o que a linha diz.
    /// </summary>
    [Fact]
    public async Task A_coherent_partial_return_confirms_and_gives_back_its_own_quantity()
    {
        var (contract, _, returnInvoice) = await SeedAsync();

        var item = await _db.Context.SalesInvoicesItems
            .SingleAsync(x => x.SalesInvoiceKey == returnInvoice.Key);

        item.Quantity = 20m;
        await ItemsUpdateService().ExecuteAsync(item.Key!.Value, item, "tester");

        await ConfirmService().ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await InvoiceAsync(returnInvoice.Key)).InvoiceStatus);
        Assert.Equal(10m, (await ContractAsync(_db, contract.Key)).AllocatedVolume);
    }
}
