using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1181 fase 2, Task 5 — vincular a Expedição de Grãos (<see cref="StorageTransactionType.SalesShipment"/>,
/// o 7) que fecha um transbordo em armazém PRÓPRIO. É o último passo do fluxo: a saída do LOTE
/// (Task 4, <see cref="ShipmentLoadsTransshipmentAttachLotExitService"/>) já vinculou o
/// <c>Shipment (1)</c> e emitiu a liberação; vincular esta Expedição a essa liberação, pelo
/// caminho de sempre (<see cref="ShipmentLoadsAttachTransactionsService"/>), conclui o transbordo
/// e torna a carga faturável.
/// </summary>
/// <remarks>
/// O primeiro teste é de CARACTERIZAÇÃO: a validação de papel de hoje
/// (<c>ValidateTransshipmentRoleAsync</c>) já exige só que o armazém do romaneio bata com o do
/// transbordo e que a entrada esteja registrada — o que já vale para armazém próprio sem mudança
/// nenhuma de produção. O segundo prova a regra nova: recusar antes de a saída do lote (Task 4)
/// estar vinculada, porque é ela que emite a liberação que esta Expedição consumiria — sem ela, a
/// Expedição só poderia estar consumindo liberação de outro negócio.
/// <para>
/// Round 1 da revisão da Task 6: cogitou-se trocar o discriminador armazém próprio/terceiro para
/// <c>WarehouseComplement.IsOwn</c> (o mesmo já usado por <c>EnsureWarehouseAcceptsTransshipmentAsync</c>,
/// removido na fase 2, e por <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>), mas a
/// decisão final foi MANTER o
/// discriminador estrutural (o TIPO do romaneio de entrada) — ver a regra completa em
/// <see cref="ShipmentLoadTransshipmentRules"/>. O terceiro teste continua valendo como rede dessa
/// checagem: prova que uma entrada <c>TransshipmentReceipt</c> REAL (armazém de terceiro) segue
/// fechando o transbordo mesmo sem a saída do lote, que é o comportamento da fase 1 já em produção.
/// </para>
/// </remarks>
public class ShipmentLoadsAttachOwnWarehouseExitTests
{
    private const string OriginWarehouse = "ARM01";
    private const string TransshipmentWarehouse = "ARM99";

    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsAttachTransactionsService AttachService() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(decimal totalQuantity = 30_000m)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = totalQuantity,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    /// <summary>Romaneio de saída da ORIGEM da carga — o que rastreia o contrato de compra.</summary>
    private StorageTransaction OriginShipment(ShipmentLoad load, decimal grossWeight = 30_000m)
    {
        var origin = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R0001",
            CardCode = "C0001",
            ItemCode = load.ItemCode,
            UnitOfMeasureCode = load.UnitOfMeasureCode,
            WarehouseCode = OriginWarehouse,
            BranchCode = load.BranchCode,
            TruckCode = load.TruckCode,
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };

        _db.Context.StorageTransactions.Add(origin);
        return origin;
    }

    /// <summary>
    /// Entrada do transbordo em armazém PRÓPRIO (Task 3): o <c>Receipt (0)</c> da pesagem — é este
    /// TIPO que distingue o armazém próprio do de terceiro (cujo <c>EntryStorageTransaction</c> é
    /// o <c>TransshipmentReceipt</c>, o 15).
    /// </summary>
    private StorageTransaction OwnWarehouseEntryReceipt(decimal grossWeight = 30_000m)
    {
        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        _db.Context.StorageTransactions.Add(receipt);
        return receipt;
    }

    /// <summary>
    /// Entrada do transbordo em armazém de TERCEIRO (Task 3, fase 1): o
    /// <c>TransshipmentReceipt (15)</c> criado diretamente pela entrada — linha REAL na tabela
    /// (não chave fantasma), sem <c>Receipt</c> de pesagem nem lote nenhum por trás.
    /// </summary>
    private StorageTransaction ThirdPartyEntryReceipt(decimal grossWeight = 30_000m)
    {
        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0003",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        _db.Context.StorageTransactions.Add(receipt);
        return receipt;
    }

    /// <summary>A Expedição de Grãos (<c>SalesShipment</c>, o 7) que vai ser vinculada à carga.</summary>
    private StorageTransaction Expedicao(decimal grossWeight = 29_000m)
    {
        var exit = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "S0002",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = TransshipmentWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
        };

        _db.Context.StorageTransactions.Add(exit);
        return exit;
    }

    private ShipmentLoadTransshipment Transshipment(
        ShipmentLoad load, StorageTransaction entryReceipt, Guid? lotExitKey, decimal outgoingQuantity = 30_000m)
    {
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = 1,
            WarehouseCode = TransshipmentWarehouse,
            WarehouseName = "ARMAZEM PROPRIO",
            OutgoingQuantity = outgoingQuantity,
            EntryStorageTransactionKey = entryReceipt.Key,
            LotExitStorageTransactionKey = lotExitKey,
        };

        _db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        return transshipment;
    }

    /// <summary>
    /// CARACTERIZAÇÃO: a validação de papel já existente (armazém do romaneio == armazém do
    /// transbordo, entrada registrada) já é suficiente para fechar o transbordo em armazém
    /// PRÓPRIO — sem mudança nenhuma de produção. Cenário completo do estado em que a Task 4 deixa
    /// a carga: entrada registrada E saída do lote já vinculada.
    /// </summary>
    [Fact]
    public async Task Attach_OwnWarehouseExit_ClosesTheTransshipmentAndMakesTheLoadBillable()
    {
        var load = Load();
        OriginShipment(load);
        var entry = OwnWarehouseEntryReceipt();
        var transshipment = Transshipment(load, entry, lotExitKey: Guid.NewGuid());
        var expedicao = Expedicao();
        await _db.Context.SaveChangesAsync();

        await AttachService().ExecuteAsync(load.Key, [expedicao.Key], transshipment.Key, "tester");

        var savedExit = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == expedicao.Key);
        Assert.Equal(load.Key, savedExit.ShipmentLoadKey);
        Assert.Equal(transshipment.Key, savedExit.ShipmentLoadTransshipmentKey);

        // Fecha o transbordo: HasOpenTransshipmentAsync só enxerga o tipo SalesShipment, e agora
        // existe um vinculado ao transbordo.
        Assert.False(
            await ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync(_db.Context, load.Key));

        // Torna a carga faturável: sem transbordo aberto e nada faturado/devolvido ainda,
        // ResolveStatus resolve para Open — não basta "diferente de InTransshipment", que passaria
        // com qualquer outro estado errado.
        var reloadedLoad = await _db.Context.ShipmentLoads
            .AsNoTracking()
            .SingleAsync(x => x.Key == load.Key);
        Assert.Equal(ShipmentLoadStatus.Open, reloadedLoad.Status);
    }

    /// <summary>
    /// Regra nova: a liberação que esta Expedição consumiria nasce exatamente quando a saída do
    /// lote (Task 4) é vinculada. Sem ela, o único vínculo que existiria seria com uma liberação de
    /// OUTRO negócio — o buraco que esta fase existe para fechar. Mesmo cenário completo e válido
    /// do teste de caracterização acima (entrada registrada, armazém certo, Expedição confirmada),
    /// mudando só <c>LotExitStorageTransactionKey</c> para nulo, para que a ÚNICA razão da recusa
    /// seja a regra nova.
    /// </summary>
    [Fact]
    public async Task Attach_OwnWarehouseExit_RefusedBeforeTheLotExitIsAttached()
    {
        var load = Load();
        OriginShipment(load);
        var entry = OwnWarehouseEntryReceipt();
        var transshipment = Transshipment(load, entry, lotExitKey: null);
        var expedicao = Expedicao();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => AttachService().ExecuteAsync(load.Key, [expedicao.Key], transshipment.Key, "tester"));

        Assert.Contains("saída do lote", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(
            (await _db.Context.StorageTransactions.SingleAsync(x => x.Key == expedicao.Key)).ShipmentLoadKey);
    }

    /// <summary>
    /// Rede do discriminador estrutural (mantido no Round 1 da revisão da Task 6 — ver
    /// <see cref="ShipmentLoadTransshipmentRules"/>): um armazém de TERCEIRO — cuja
    /// <c>EntryStorageTransaction</c> é um <c>TransshipmentReceipt (15)</c> REAL, linha de verdade
    /// na tabela, não a chave fantasma que os testes de
    /// <see cref="ShipmentLoadsAttachTransshipmentExitTests"/> usam — continua fechando o
    /// transbordo mesmo com <c>LotExitStorageTransactionKey</c> nulo, porque nesse armazém a
    /// liberação já nasceu na entrada (Task 3, fase 1). Sem este teste, tornar a regra
    /// incondicional (aplicá-la também quando a entrada não é um <c>Receipt</c>) travaria para
    /// sempre o fechamento de transbordo em armazém de terceiro — a fase 1, já em produção.
    /// </summary>
    [Fact]
    public async Task Attach_ThirdPartyWarehouseExit_SucceedsWithoutTheLotExit()
    {
        var load = Load();
        OriginShipment(load);
        var entry = ThirdPartyEntryReceipt();
        var transshipment = Transshipment(load, entry, lotExitKey: null);
        var expedicao = Expedicao();
        await _db.Context.SaveChangesAsync();

        await AttachService().ExecuteAsync(load.Key, [expedicao.Key], transshipment.Key, "tester");

        var savedExit = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == expedicao.Key);
        Assert.Equal(load.Key, savedExit.ShipmentLoadKey);
        Assert.Equal(transshipment.Key, savedExit.ShipmentLoadTransshipmentKey);
    }
}
