using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Estado do documento de saída de ORIGEM ao longo do ciclo de retorno.
///
/// A regra é: cada efeito é desfeito no MESMO nível em que foi aplicado.
/// O status "Retornado" e o fechamento da entrega da origem nascem com a CRIAÇÃO do retorno
/// (<see cref="SalesInvoicesReturnService"/>), então só a EXCLUSÃO ou o CANCELAMENTO do
/// retorno os desfazem. O estorno de confirmação desfaz apenas o que a confirmação aplicou
/// (os romaneios) — a origem continua retornada, porque o documento de retorno continua
/// existindo.
/// </summary>
public class SalesInvoicesReturnOriginStateTests
{
    private static SalesInvoicesConfirmService Confirm(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(
                new UsageService(db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static SalesInvoicesCancelService Cancel(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private static SalesInvoicesDeleteService Delete(UnitOfWork db) =>
        new(db,
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesDeleteService>.Instance);

    /// <summary>
    /// Origem com uma linha entregue, e o retorno correspondente apontando para ela pela
    /// chave de origem — do cabeçalho e da linha. O default reproduz o estado logo após o
    /// "Retornar": origem já retornada e com a entrega fechada.
    /// </summary>
    private static async Task<(SalesInvoice Origin, SalesInvoice Return)> SeedAsync(
        UnitOfWork db,
        InvoiceStatus originStatus = InvoiceStatus.Returned,
        SalesInvoiceDeliveryStatus originDelivery = SalesInvoiceDeliveryStatus.Closed,
        InvoiceStatus returnStatus = InvoiceStatus.Pending)
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice(originStatus);
        origin.DeliveryStatus = originDelivery;

        var originItem = SalesContractsAllocationTestSupport.NewItem(
            origin, contractKey: null, releaseKey: null, quantity: 100m);
        originItem.DeliveredQuantity = 100m;
        originItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        var returnInvoice = SalesContractsAllocationTestSupport.NewInvoice(
            returnStatus, SalesInvoiceType.Return, originKey: origin.Key);

        SalesContractsAllocationTestSupport.NewItem(
            returnInvoice, contractKey: null, releaseKey: null, quantity: 100m,
            originItemKey: originItem.Key);

        // O peso do cabeçalho de uma devolução é a soma das linhas — montá-la à mão sem ele
        // produziria um documento que a confirmação recusa, e que nenhum caminho real cria.
        SalesInvoicesReturnWeightService.Apply(returnInvoice);

        db.Context.SalesInvoices.AddRange(origin, returnInvoice);
        await db.SaveChangesAsync();

        return (origin, returnInvoice);
    }

    private static async Task<SalesInvoice> InvoiceAsync(UnitOfWork db, Guid key) =>
        await db.Context.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Key == key);

    /// <summary>
    /// Fluxo NOVO do estorno: reconhecido por haver romaneio apontando para o retorno.
    /// </summary>
    private static async Task SeedReturnedTransactionAsync(
        UnitOfWork db, SalesInvoice origin, SalesInvoice returnInvoice)
    {
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "01",
            NetWeight = 100m,
            TransactionStatus = StorageTransactionsStatus.Returned,
            ReturnInvoiceKey = returnInvoice.Key,
            SalesInvoiceKey = origin.Key,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Confirmar o retorno é o que o torna efetivo, então é ele quem garante o "Retornado" na
    /// origem — idempotente, para o estado se autocorrigir em documentos antigos.
    /// </summary>
    [Fact]
    public async Task Confirming_a_return_marks_the_origin_as_returned()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db, originStatus: InvoiceStatus.Confirmed);

        await Confirm(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Returned, (await InvoiceAsync(db, origin.Key)).InvoiceStatus);
    }

    /// <summary>
    /// Estornar devolve o retorno para Pendente, mas o documento de retorno CONTINUA
    /// existindo — logo a origem continua retornada. Foi exatamente isto que deixou a origem
    /// 5955 presa em "Confirmada" em produção depois de um estorno.
    /// </summary>
    [Fact]
    public async Task Reversing_a_return_keeps_the_origin_returned()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db, returnStatus: InvoiceStatus.Confirmed);
        await SeedReturnedTransactionAsync(db, origin, returnInvoice);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var reversed = await InvoiceAsync(db, origin.Key);

        Assert.Equal(InvoiceStatus.Returned, reversed.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, reversed.DeliveryStatus);
    }

    /// <summary>
    /// Mesmo contrato no fluxo LEGADO do estorno (nenhum romaneio aponta para o retorno).
    /// </summary>
    [Fact]
    public async Task Reversing_a_legacy_return_keeps_the_origin_returned()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db, returnStatus: InvoiceStatus.Confirmed);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Returned, (await InvoiceAsync(db, origin.Key)).InvoiceStatus);
    }

    /// <summary>
    /// Cancelar o retorno faz o documento deixar de valer: a origem volta a "Confirmada" e a
    /// entrega reabre, senão ela recusaria um novo retorno com "Invoice closed.".
    /// </summary>
    [Fact]
    public async Task Cancelling_a_return_restores_the_origin_to_confirmed()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db);

        await Cancel(db).ExecuteAsync(returnInvoice.Key, "tester");

        var restored = await InvoiceAsync(db, origin.Key);

        Assert.Equal(InvoiceStatus.Confirmed, restored.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, restored.DeliveryStatus);
        Assert.Equal(0m, restored.Items.Single().DeliveredQuantity);
    }

    /// <summary>
    /// Excluir tem o mesmo efeito do cancelamento sobre a origem — o retorno deixa de existir.
    /// </summary>
    [Fact]
    public async Task Deleting_a_return_restores_the_origin_to_confirmed()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db);

        await Delete(db).ExecuteAsync(returnInvoice.Key, "tester");

        var restored = await InvoiceAsync(db, origin.Key);

        Assert.Equal(InvoiceStatus.Confirmed, restored.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, restored.DeliveryStatus);
        Assert.Equal(0m, restored.Items.Single().DeliveredQuantity);
    }

    /// <summary>
    /// Cancelar um documento NORMAL não pode encostar em origem nenhuma — a restauração é
    /// exclusiva do documento de retorno.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_normal_invoice_leaves_other_documents_alone()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, _) = await SeedAsync(db);

        var unrelated = SalesContractsAllocationTestSupport.NewInvoice();
        SalesContractsAllocationTestSupport.NewItem(
            unrelated, contractKey: null, releaseKey: null, quantity: 10m);
        db.Context.SalesInvoices.Add(unrelated);
        await db.SaveChangesAsync();

        await Cancel(db).ExecuteAsync(unrelated.Key, "tester");

        Assert.Equal(InvoiceStatus.Returned, (await InvoiceAsync(db, origin.Key)).InvoiceStatus);
    }

    /// <summary>
    /// Reabrir a entrega da origem muda o FATOR EFETIVO: a quebra apurada só é descontada do
    /// contrato enquanto o item está <c>Closed</c>. O retorno recalcula os contratos ao fechar
    /// os itens (<c>SalesInvoicesReturnService:69</c>); o cancelamento precisa fazer o caminho
    /// de volta, senão o saldo do contrato fica congelado no fator do item fechado.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_return_recalculates_the_contract_balance()
    {
        var db = TestDb.CreateUnitOfWork();
        var (origin, returnInvoice) = await SeedAsync(db);

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000m);
        var originItem = origin.Items.Single();

        // Perda de 5 apurada na conferência: com o item fechado, o contrato consome 95.
        originItem.QuantityLoss = 5m;

        var allocation = SalesContractsAllocationTestSupport.NewAllocation(
            contract.Key, originItem.Key!.Value, volume: 100m);
        allocation.OwnsDeliveryDifference = true;
        contract.AllocatedVolume = 95m;

        db.Context.SalesContracts.Add(contract);
        db.Context.SalesContractsAllocations.Add(allocation);
        await db.SaveChangesAsync();

        await Cancel(db).ExecuteAsync(returnInvoice.Key, "tester");

        // Entrega reaberta: não há quebra a descontar, o contrato volta ao volume nominal.
        Assert.Equal(100m,
            (await SalesContractsAllocationTestSupport.ContractAsync(db, contract.Key))
            .AllocatedVolume);
    }
}
