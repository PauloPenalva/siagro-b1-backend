using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Estorno da confirmação de uma devolução nascida de CARGA.
/// </summary>
/// <remarks>
/// O documento de carga tem <c>SalesTransactions</c> vazia — ele não conhece romaneio, chega
/// neles pela carga —, então a confirmação da devolução nunca grava <c>ReturnInvoiceKey</c> em
/// romaneio nenhum. O discriminador <c>isNewFlow</c> do estorno é exatamente essa coluna, de
/// modo que TODA devolução de carga caía no ramo LEGADO, cuja consulta de "órfãos" casa por
/// cliente e produto e sequestra romaneio solto de outro carregamento.
/// </remarks>
public class SalesInvoicesReverseConfirmLoadReturnTests
{
    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static ShipmentLoad NewLoad(UnitOfWork db)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000042",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            BranchCode = "01",
            TotalQuantity = 40_000m,
            InvoicedQuantity = 40_000m,
            Status = ShipmentLoadStatus.Invoiced,
        };

        db.Context.ShipmentLoads.Add(load);
        return load;
    }

    /// <summary>
    /// Romaneio SOLTO de outro carregamento: sem nota, sem carga, confirmado, mesmo cliente e
    /// mesmo produto da origem. É exatamente o que a consulta de órfãos do ramo legado captura.
    /// </summary>
    private static StorageTransaction NewLooseShipment(UnitOfWork db)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000999",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            GrossWeight = 31_000m,
            NetWeight = 31_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            SalesInvoiceKey = null,
            ShipmentLoadKey = null,
        };

        db.Context.StorageTransactions.Add(shipment);
        return shipment;
    }

    private static async Task<(SalesInvoice Origin, SalesInvoice Return)> SeedLoadReturnAsync(
        UnitOfWork db, ShipmentLoad load)
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice(InvoiceStatus.Returned);
        origin.ShipmentLoadKey = load.Key;
        origin.InvoiceNumber = "000000501";

        var originItem = SalesContractsAllocationTestSupport.NewItem(
            origin, contractKey: null, releaseKey: null, quantity: 40_000m);

        var returnInvoice = SalesContractsAllocationTestSupport.NewInvoice(
            InvoiceStatus.Confirmed, SalesInvoiceType.Return, originKey: origin.Key);
        returnInvoice.ShipmentLoadKey = load.Key;
        returnInvoice.InvoiceNumber = "000000502";

        var returnItem = SalesContractsAllocationTestSupport.NewItem(
            returnInvoice, contractKey: null, releaseKey: null, quantity: 40_000m,
            originItemKey: originItem.Key);
        returnItem.DeliveredQuantity = 40_000m;
        returnItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        db.Context.SalesInvoices.AddRange(origin, returnInvoice);
        await db.SaveChangesAsync();

        return (origin, returnInvoice);
    }

    /// <summary>
    /// A regressão: o romaneio solto de OUTRO carregamento não pode ser tocado pelo estorno.
    /// Antes da correção ele saía daqui carimbado como <c>Invoiced</c> e apontando para a nota
    /// de origem desta carga.
    /// </summary>
    [Fact]
    public async Task Reversing_a_load_return_does_not_hijack_a_loose_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);
        var loose = NewLooseShipment(db);
        var (_, returnInvoice) = await SeedLoadReturnAsync(db, load);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var untouched = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.Key == loose.Key);

        Assert.Null(untouched.SalesInvoiceKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, untouched.TransactionStatus);
        Assert.False(untouched.IsInvoiced);
        Assert.Equal(0m, untouched.InvoiceQty);
    }

    /// <summary>
    /// O romaneio DA carga também não é reescrito pelo estorno: quem manda no
    /// <c>TransactionStatus</c> dele é <c>ShipmentLoadsRecalculateInvoicedService</c>.
    /// </summary>
    [Fact]
    public async Task Reversing_a_load_return_does_not_touch_the_loads_own_shipments()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);

        var shipment = NewLooseShipment(db);
        shipment.Code = "RM000500";
        shipment.ShipmentLoadKey = load.Key;
        shipment.TransactionStatus = StorageTransactionsStatus.Invoiced;

        var (_, returnInvoice) = await SeedLoadReturnAsync(db, load);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var reloaded = await db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.Key == shipment.Key);

        Assert.Null(reloaded.SalesInvoiceKey);
        Assert.Equal(load.Key, reloaded.ShipmentLoadKey);
        Assert.Null(reloaded.ReturnInvoiceKey);
    }

    /// <summary>
    /// O contrato do ramo continua o dos outros dois: o estorno devolve a devolução para
    /// Pendente e reabre os itens DELA, sem desfazer o que a criação do retorno aplicou na
    /// origem.
    /// </summary>
    [Fact]
    public async Task Reversing_a_load_return_reopens_the_return_items_and_keeps_the_origin_returned()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);
        var (origin, returnInvoice) = await SeedLoadReturnAsync(db, load);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var reversed = await db.Context.SalesInvoices
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleAsync(x => x.Key == returnInvoice.Key);

        Assert.Equal(InvoiceStatus.Pending, reversed.InvoiceStatus);
        Assert.Equal(0m, reversed.Items.Single().DeliveredQuantity);
        Assert.Equal(SalesInvoiceDeliveryStatus.Open, reversed.Items.Single().DeliveryStatus);

        var originAfter = await db.Context.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Key == origin.Key);

        Assert.Equal(InvoiceStatus.Returned, originAfter.InvoiceStatus);
    }

    /// <summary>
    /// E o saldo da carga volta a ser consumido: a devolução em Pendente deixa de abater a
    /// origem na fórmula.
    /// </summary>
    [Fact]
    public async Task Reversing_a_load_return_reconsumes_the_load_balance()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);
        load.InvoicedQuantity = 0m;
        load.Status = ShipmentLoadStatus.Open;

        var (_, returnInvoice) = await SeedLoadReturnAsync(db, load);

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var reloaded = await db.Context.ShipmentLoads
            .AsNoTracking()
            .SingleAsync(x => x.Key == load.Key);

        Assert.Equal(40_000m, reloaded.InvoicedQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, reloaded.Status);
    }

    /// <summary>
    /// Não se estorna a devolução de uma recusa cuja mercadoria já foi descarregada num armazém.
    /// </summary>
    /// <remarks>
    /// O descarregamento é um fato FÍSICO consumado — o grão está no armazém de destino, creditado
    /// no saldo dele. Desfazê-lo pela tela de documentos deixa a carga com o mesmo volume contado
    /// duas vezes: <c>Invoiced</c> volta a consumir e <c>ReturnedToWarehouse</c> continua de pé,
    /// levando o saldo disponível a NEGATIVO. A carga então não pode ser faturada (guard de
    /// faturamento), não pode ter a composição alterada (guard de composição) e não tem caminho de
    /// volta — um estado sem saída pela tela.
    /// <para>
    /// A trava é pela existência de devolução ao armazém viva NA CARGA, e não por qual retorno
    /// está sendo estornado: a entrada de armazém é uma por RECUSA (não por documento), então ela
    /// não aponta um retorno específico que se pudesse desfazer isoladamente.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Reversing_a_load_return_is_refused_when_the_goods_went_back_to_a_warehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);

        var (_, returnInvoice) = await SeedLoadReturnAsync(db, load);

        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000778",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            RefusedFromShipmentLoadKey = load.Key,
        });

        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => Reverse(db).ExecuteAsync(returnInvoice.Key, "tester"));

        Assert.Contains("ARM99", error.Message);

        // E nada foi aplicado pela metade: a devolução continua confirmada.
        var untouched = await db.Context.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Key == returnInvoice.Key);

        Assert.Equal(InvoiceStatus.Confirmed, untouched.InvoiceStatus);
    }

    /// <summary>
    /// Contraprova: uma devolução ao armazém CANCELADA não trava mais nada — ela já foi desfeita,
    /// e o grão não está mais creditado.
    /// </summary>
    [Fact]
    public async Task A_cancelled_warehouse_return_does_not_block_the_reversal()
    {
        var db = TestDb.CreateUnitOfWork();
        var load = NewLoad(db);

        var (_, returnInvoice) = await SeedLoadReturnAsync(db, load);

        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "RM000781",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM99",
            BranchCode = "01",
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
            TransactionType = StorageTransactionType.SalesShipmentReturn,
            TransactionStatus = StorageTransactionsStatus.Cancelled,
            RefusedFromShipmentLoadKey = load.Key,
        });

        await db.SaveChangesAsync();

        await Reverse(db).ExecuteAsync(returnInvoice.Key, "tester");

        var reversed = await db.Context.SalesInvoices
            .AsNoTracking()
            .SingleAsync(x => x.Key == returnInvoice.Key);

        Assert.Equal(InvoiceStatus.Pending, reversed.InvoiceStatus);
    }
}
