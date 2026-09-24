using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Vinculação de romaneios à carga. Cobre a invariante I1 — "um romaneio, uma carga" — e a
/// homogeneidade, que aqui é comparada contra a CARGA, não entre os romaneios selecionados.
/// </summary>
/// <remarks>
/// A maior parte destes casos veio de <c>ShipmentLoadsCreateServiceTests</c>: eram os guards da
/// montagem, e continuam valendo palavra por palavra — só mudaram de serviço junto com o passo
/// que eles protegem.
/// </remarks>
public class ShipmentLoadsAttachTransactionsServiceTests
{
    private readonly IUnitOfWork _db = TestDb.CreateUnitOfWork();

    private ShipmentLoadsAttachTransactionsService Service() => new(
        _db, new ShipmentLoadsMovementLogService(_db.Context));

    private ShipmentLoad Load(ShipmentLoadStatus status = ShipmentLoadStatus.Planned)
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
            WarehouseCode = "ARM01",
            Status = status,
            TotalQuantity = decimal.Zero,
        };

        _db.Context.ShipmentLoads.Add(load);
        return load;
    }

    private StorageTransaction Shipment(
        string code,
        decimal grossWeight = 30_000,
        string truckCode = "ABC1D23",
        string itemCode = "SOJA",
        string branchCode = "01",
        string unitOfMeasureCode = "KG",
        StorageTransactionType type = StorageTransactionType.SalesShipment,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        Guid? shipmentLoadKey = null)
    {
        var transaction = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C001",
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = unitOfMeasureCode,
            WarehouseCode = "ARM01",
            WarehouseName = "ARMAZEM 01",
            BranchCode = branchCode,
            TruckCode = truckCode,
            TruckDriverCode = "M001",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = type,
            TransactionStatus = status,
            ShipmentLoadKey = shipmentLoadKey,
        };

        _db.Context.StorageTransactions.Add(transaction);
        return transaction;
    }

    [Fact]
    public async Task Attaching_shipments_fills_the_load_and_opens_it()
    {
        var load = Load();
        var a = Shipment("R1", 30_000);
        var b = Shipment("R2", 31_500);
        var c = Shipment("R3", 28_500);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key, b.Key, c.Key], null, "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        // Soma o BRUTO, e não o líquido: é o número que vira a quantidade da nota.
        Assert.Equal(90_000m, saved.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        Assert.Equal(90_000m, saved.AvailableQuantity);

        var linked = await _db.Context.StorageTransactions
            .CountAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(3, linked);
    }

    [Fact]
    public async Task Attaching_records_a_movement_with_the_attached_quantity()
    {
        var load = Load();
        var a = Shipment("R1", 30_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], null, "tester");

        var movement = await _db.Context.ShipmentLoadMovements
            .SingleAsync(x => x.ShipmentLoadKey == load.Key);

        Assert.Equal(ShipmentLoadMovementType.TransactionsAttached, movement.MovementType);
        Assert.Equal(30_000m, movement.Quantity);
        Assert.Equal(30_000m, movement.BalanceAfter);
        Assert.Contains("R1", movement.Description);
    }

    /// <summary>
    /// Vincular a uma carga que já tem romaneios soma ao que existe, em vez de substituir.
    /// </summary>
    [Fact]
    public async Task Attaching_again_adds_to_the_existing_total()
    {
        var load = Load();
        var a = Shipment("R1", 30_000);
        var b = Shipment("R2", 20_000);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key], null, "tester");
        await Service().ExecuteAsync(load.Key, [b.Key], null, "tester");

        Assert.Equal(50_000m, (await _db.Context.ShipmentLoads.SingleAsync()).TotalQuantity);
    }

    [Fact]
    public async Task Refuses_an_empty_selection()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [], null, "tester"));

        Assert.Contains("romaneio", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_shipment_from_a_different_truck()
    {
        var load = Load();
        var a = Shipment("R1", truckCode: "XYZ9W87");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("veículo", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(decimal.Zero, (await _db.Context.ShipmentLoads.SingleAsync()).TotalQuantity);
    }

    [Fact]
    public async Task Refuses_a_shipment_of_a_different_item()
    {
        var load = Load();
        var a = Shipment("R1", itemCode: "MILHO");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("produto", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_a_shipment_of_a_different_branch()
    {
        var load = Load();
        var a = Shipment("R1", branchCode: "02");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("filial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A soma de GrossWeight é adimensional: misturar unidades produziria um total sem
    /// significado, e é ele que vira a quantidade da nota.
    /// </summary>
    [Fact]
    public async Task Refuses_a_shipment_in_a_different_unit_of_measure()
    {
        var load = Load();
        var a = Shipment("R1", unitOfMeasureCode: "TON");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("TON", error.Message);
    }

    [Fact]
    public async Task Refuses_a_shipment_already_in_another_load_naming_both_codes()
    {
        var load = Load();

        var other = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000042",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
        };
        _db.Context.ShipmentLoads.Add(other);

        var a = Shipment("R1", shipmentLoadKey: other.Key);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        // Nomear os dois códigos é o que torna o erro acionável: o usuário precisa saber
        // QUAL romaneio e em QUAL carga ele já está.
        Assert.Contains("R1", error.Message);
        Assert.Contains("CG000042", error.Message);
    }

    /// <summary>
    /// Expedição ORIGINAL substituída por uma troca de liberação (GAC-1177) não pode ser
    /// vinculada a uma carga de novo por aqui — quem controla esse romaneio é o fluxo da troca.
    /// </summary>
    [Fact]
    public async Task Refuses_a_shipment_replaced_by_a_release_change()
    {
        var load = Load();
        var a = Shipment("R1");
        a.ReplacedByShippingReleaseChangeKey = Guid.NewGuid();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("R1", error.Message);
        Assert.Contains("troca de liberação", error.Message);
    }

    [Fact]
    public async Task Refuses_a_shipment_that_is_not_confirmed()
    {
        var load = Load();
        var a = Shipment("R1", status: StorageTransactionsStatus.Pending);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("R1", error.Message);
    }

    [Fact]
    public async Task Refuses_a_transaction_that_is_not_a_shipment()
    {
        var load = Load();
        var a = Shipment("R1", type: StorageTransactionType.Purchase);
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("R1", error.Message);
    }

    [Fact]
    public async Task Refuses_a_key_that_does_not_exist()
    {
        var load = Load();
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [Guid.NewGuid()], null, "tester"));

        Assert.Contains("não encontrado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Aumentar o volume de uma carga já faturada mexeria no denominador do guard de
    /// faturamento e faria o laço de projeção carimbar o romaneio novo como <c>Invoiced</c>
    /// sem que nada dele tenha sido faturado.
    /// </summary>
    [Theory]
    [InlineData(ShipmentLoadStatus.PartiallyInvoiced)]
    [InlineData(ShipmentLoadStatus.Invoiced)]
    [InlineData(ShipmentLoadStatus.Discharged)]
    [InlineData(ShipmentLoadStatus.Cancelled)]
    public async Task Refuses_to_attach_to_a_load_that_is_no_longer_open(ShipmentLoadStatus status)
    {
        var load = Load(status);
        var a = Shipment("R1");
        await _db.Context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("CG000001", error.Message);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync()).ShipmentLoadKey);
    }
    /// <summary>
    /// GAC-1175: cada natureza de carga aceita um tipo de romaneio, e só ele. A carga Normal
    /// move mercadoria para FORA (embarque de venda); a de Remoção traz para DENTRO
    /// (recebimento).
    /// </summary>
    [Fact]
    public async Task Attaching_a_shipment_to_a_removal_load_is_refused()
    {
        var load = Load();
        load.LoadType = ShipmentLoadType.Removal;
        var a = Shipment("R1");
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("recebimento", ex.Message);
        Assert.Contains("R1", ex.Message);
    }

    [Fact]
    public async Task Attaching_a_receipt_to_a_normal_load_is_refused()
    {
        var load = Load();
        var a = Shipment("R1", type: StorageTransactionType.Receipt);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("embarque", ex.Message);
    }

    [Fact]
    public async Task Attaching_receipts_to_a_removal_load_fills_it_and_opens_it()
    {
        var load = Load();
        load.LoadType = ShipmentLoadType.Removal;
        var a = Shipment("R1", 30_000, type: StorageTransactionType.Receipt);
        var b = Shipment("R2", 20_000, type: StorageTransactionType.Receipt);
        await _db.Context.SaveChangesAsync();

        await Service().ExecuteAsync(load.Key, [a.Key, b.Key], null, "tester");

        var saved = await _db.Context.ShipmentLoads.SingleAsync();
        Assert.Equal(50_000m, saved.TotalQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, saved.Status);
        // A remoção não fatura: o saldo não pode nascer como "a faturar".
        Assert.Equal(decimal.Zero, saved.InvoicedQuantity);
    }

    /// <summary>
    /// O Recebimento só entra CONFIRMADO, como a expedição — a regra de elegibilidade é a mesma
    /// nos dois tipos de carga, e só o tipo do romaneio muda.
    /// </summary>
    [Fact]
    public async Task Attaching_a_pending_receipt_is_refused()
    {
        var load = Load();
        load.LoadType = ShipmentLoadType.Removal;
        var a = Shipment("R1", type: StorageTransactionType.Receipt,
            status: StorageTransactionsStatus.Pending);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("não está confirmado", ex.Message);
    }

    [Fact]
    public async Task Attaching_to_a_completed_removal_load_is_refused()
    {
        var load = Load(ShipmentLoadStatus.Completed);
        load.LoadType = ShipmentLoadType.Removal;
        var a = Shipment("R1", type: StorageTransactionType.Receipt);
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        Assert.Contains("reabra", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GAC-1171 (melhorias): a carga Normal é concluída pela Conferência de Entregas, não pelo
    /// "Reabrir". A mensagem manda a pessoa ao caminho de volta certo.
    /// </summary>
    [Fact]
    public async Task Attaching_to_a_completed_normal_load_asks_to_reverse_the_reconciliation()
    {
        var load = Load(ShipmentLoadStatus.Completed);
        var a = Shipment("R1");
        await _db.Context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(load.Key, [a.Key], null, "tester"));

        // Mensagem exata: a Concluída tem frase própria, sem o sufixo "Cancele os documentos de
        // saída…" da carga Normal faturada, que daria ao usuário uma segunda dica contraditória.
        Assert.Equal(
            "A carga CG000001 já foi concluída e não aceita novos romaneios. " +
            "Estorne a conferência de entrega antes de alterar a composição.",
            ex.Message);
        Assert.Null((await _db.Context.StorageTransactions.SingleAsync()).ShipmentLoadKey);
    }

}
